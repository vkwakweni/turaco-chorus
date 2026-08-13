#if DEBUG
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus;

internal static class DevSeedData
{
    public static async Task UseDevelopmentSeedDataAsync(this WebApplication app)
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

        // Ideally, this matches the Answer output, but no verification happens here
        logDataSource.Seed(userId, new AggregateStats(
            SourceId: userId,
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 3,
            Categories: [new CategoryCount("mood", 3)],
            EntriesByDate: [new DateCount(new DateOnly(2026, 1, 15), 3)]));

        // /ask requires consent; FakeConsentStore correctly defaults to not-granted otherwise.
        var consentStore = app.Services.GetRequiredService<IConsentStore>();
        await consentStore.SetConsentAsync(userId, granted: true);

        // FakeInsightEngine.AnswerToReturn has default values that are technically valid,
        // but not a believable-looking demo answer.
        var insightEngine = (FakeInsightEngine)app.Services.GetRequiredService<IInsightEngine>();
        insightEngine.AnswerToReturn = new Answer(
            Text: "You logged 3 mood entries in January 2026.",
            DataUsed: new DataUsed(
                StatsQueried: ["mood"],
                Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))));
    }
}
#endif
