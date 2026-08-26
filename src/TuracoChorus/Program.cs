using TuracoChorus;
using TuracoChorus.Auth;
using TuracoChorus.Contracts;
using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Ports;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddPortAdapters();

builder.Services.AddScoped<StatsOrchestrator>();
builder.Services.AddScoped<ConsentOrchestrator>();
builder.Services.AddScoped<AskOrchestrator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
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
        _ => Results.Problem("Unexpected AskResult type.")
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
