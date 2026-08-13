using TuracoChorus;
using TuracoChorus.Auth;
using TuracoChorus.Contracts;
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Ports;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Port implementations
    // TODO: fakes for now (Phase 2); Phase 3 swaps these for real adapters.
// Singleton: each fake's in-memory state (e.g. consent, audit entries) must persist across requests.
builder.Services.AddSingleton<IIdentityVerifier, FakeIdentityVerifier>();
builder.Services.AddSingleton<IConsentStore, FakeConsentStore>();
builder.Services.AddSingleton<ILogDataSource, FakeLogDataSource>();
builder.Services.AddSingleton<IInsightEngine, FakeInsightEngine>();
builder.Services.AddSingleton<IAuditLogger, FakeAuditLogger>();

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
    app.UseDevelopmentSeedData();
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
    var userId = await BearerAuth.AuthenticateAsync(request, identityVerifier);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var stats = await orchestrator.GetStatsAsync(userId, from, to);

    return Results.Ok(new AggregateStatsResponse(
        new DateRangeResponse(stats.Range.From, stats.Range.To),
        stats.TotalEntries,
        stats.Categories.Select(c => new CategoryCountResponse(c.Name, c.Count)).ToList(),
        stats.EntriesByDate.Select(d => new DateCountResponse(d.Date, d.Count)).ToList()));
});

app.Run();
