#if DEBUG
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus;

internal static class DevSeedData
{
    public static void UseDevelopmentSeedData(this WebApplication app)
    {
        var token = app.Configuration["DevSeedData:Token"]
            ?? throw new InvalidOperationException(
                "Run: dotnet user-secrets set \"DevSeedData:Token\" \"<any-value>\"");
        var userId = app.Configuration["DevSeedData:UserId"]
            ?? throw new InvalidOperationException(
                "Run: dotnet user-secrets set \"DevSeedData:UserId\" \"<any-value>\"");

        var identityVerifier = (FakeIdentityVerifier)app.Services.GetRequiredService<IIdentityVerifier>();
        identityVerifier.Register(token, userId);

        var logDataSource = (FakeLogDataSource)app.Services.GetRequiredService<ILogDataSource>();
        logDataSource.Seed(userId, new AggregateStats(
            SourceId: userId,
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 3,
            Categories: [new CategoryCount("mood", 3)],
            EntriesByDate: [new DateCount(new DateOnly(2026, 1, 15), 3)]));
    }
}
#endif
