using TuracoChorus;
using TuracoChorus.Auth;
using TuracoChorus.Contracts;
using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Ports;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using TuracoChorus.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddPortAdapters();

builder.Services.AddScoped<StatsOrchestrator>();
builder.Services.AddScoped<ConsentOrchestrator>();
builder.Services.AddScoped<AskOrchestrator>();

// Optional: only installers calling this API directly from browser JS (rather than proxying
// through their own backend) need this at all. Comma-separated origins, e.g.
// "https://app.example.com,http://localhost:5173". Unset means no CORS policy is added.
var allowedOrigins = builder.Configuration["AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedOrigins is { Length: > 0 })
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

// Behind the proxy in a public deployment, the connection's address is the proxy's, so the real client
// address has to come from X-Forwarded-For. Off unless asked for, because anyone who can reach the app
// directly could otherwise forge the header; in a public deployment the proxy is the only way in
// (see artifacts/ecs-deployment.md).
var trustForwardedHeaders = builder.Configuration.GetValue<bool>("ForwardedHeaders:Trust");
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// /ask is the one route that spends on the AI provider; see AskRateLimitOptions for the two limits.
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = AskRateLimiting.Create(AskRateLimitOptionsReader.Read(builder.Configuration));
    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        await response.WriteAsJsonAsync(new { error = "Too many requests. Try again later." }, cancellationToken);
    };
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("UseFakeIdentityVerifier")
    || app.Configuration.GetValue<bool>("UseFakeLogDataSource"))
{
    PartialFakeSeedData.Seed(app);
}

// Configure the HTTP request pipeline.
// Registered first so it wraps everything below — see artifacts/api-contract.md's "Error responses".
app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
}));

if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
#if DEBUG
    if (app.Configuration.GetValue<bool>("UseFakeAdapters"))
    {
        await app.UseDevelopmentSeedDataAsync();
    }
#endif
}

app.UseHttpsRedirection();

if (allowedOrigins is { Length: > 0 })
{
    app.UseCors();
}

// After CORS, so a rejected request still carries the CORS headers the browser needs to read the 429.
app.UseRateLimiter();

app.MapGet("/stats", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
    IIdentityVerifier identityVerifier,
    StatsOrchestrator orchestrator) =>
{
    var auth = await BearerAuth.AuthenticateAsync(request, identityVerifier);
    if (auth is not AuthSucceeded { UserId: var userId })
    {
        return Results.Unauthorized();
    }

    var stats = await orchestrator.GetStatsAsync(userId, from, to);

    return Results.Ok(new AggregateStatsResponse(
        new DateRangeResponse(stats.Range.From, stats.Range.To),
        stats.TotalEntries,
        stats.Dimensions
            .Select(d => new DimensionResponse(
                d.Name,
                d.Buckets.Select(b => new DimensionBucketResponse(b.Value, b.Count)).ToList()))
            .ToList()));
});

app.MapPost("/ask", async (
    HttpRequest request,
    AskRequest body,
    IIdentityVerifier identityVerifier,
    AskOrchestrator orchestrator) =>
{
    var auth = await BearerAuth.AuthenticateAsync(request, identityVerifier);
    if (auth is not AuthSucceeded { UserId: var userId })
    {
        return Results.Unauthorized();
    }

    var result = await orchestrator.AskAsync(userId, body.Question);

    return result switch
    {
        AskAllowed allowed => Results.Ok(new AnswerResponse(
            allowed.Answer.Text,
            new DataUsedResponse(
                allowed.Answer.DataUsed.StatsQueried,
                new DateRangeResponse(allowed.Answer.DataUsed.Range.From, allowed.Answer.DataUsed.Range.To)))),
        AskDenied => Results.StatusCode(StatusCodes.Status403Forbidden),
        // AskAllowed/AskDenied are AskResult's only subtypes (both sealed) — this arm is
        // unreachable in practice. Throwing instead of a bespoke response lets it fall through
        // the same global exception handler as any other unhandled error, matching the
        // documented `{ "error" }` 500 shape rather than ASP.NET's default Problem+JSON.
        _ => throw new InvalidOperationException($"Unexpected AskResult subtype: {result.GetType()}")
    };
});

app.MapGet("/consent", async (
    HttpRequest request,
    IIdentityVerifier identityVerifier,
    ConsentOrchestrator orchestrator) =>
{
    var auth = await BearerAuth.AuthenticateAsync(request, identityVerifier);
    if (auth is not AuthSucceeded { UserId: var userId })
    {
        return Results.Unauthorized();
    }

    var consent = await orchestrator.GetConsentAsync(userId);

    return Results.Ok(new ConsentResponse(consent.Granted, consent.GrantedAt));
});

app.MapPut("/consent", async (
    HttpRequest request,
    ConsentRequest body,
    IIdentityVerifier identityVerifier,
    ConsentOrchestrator orchestrator) =>
{
    var auth = await BearerAuth.AuthenticateAsync(request, identityVerifier);
    if (auth is not AuthSucceeded { UserId: var userId })
    {
        return Results.Unauthorized();
    }

    var consent = await orchestrator.SetConsentAsync(userId, body.Granted);

    return Results.Ok(new ConsentResponse(consent.Granted, consent.GrantedAt));
});

app.Run();
