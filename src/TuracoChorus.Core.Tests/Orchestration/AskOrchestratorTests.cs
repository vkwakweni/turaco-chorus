using TuracoChorus.Core.Models;
using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Fakes;
using Xunit;

namespace TuracoChorus.Core.Tests.Orchestration;

public sealed class AskOrchestratorTests
{
    private static AggregateStats SomeStats(string sourceId) => new(
        SourceId: sourceId,
        Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
        TotalEntries: 3,
        Categories: [new CategoryCount("mood", 3)],
        EntriesByDate: [new DateCount(new DateOnly(2026, 1, 10), 3)]);

    [Fact]
    public async Task AskAsync_WhenConsentGranted_ReturnsTheAnswerFromInsightEngine()
    {
        // Create consent for a user
        var consentStore = new FakeConsentStore();
        await consentStore.SetConsentAsync("user-1", granted: true);

        // Create data for a user
        var logDataSource = new FakeLogDataSource();
        logDataSource.Seed("user-1", SomeStats("user-1"));

        // Create the fake standing in for the AI provider
        var insightEngine = new FakeInsightEngine();

        // Fake date return from the fake AI provider
        var extractedFrom = new DateOnly(2026, 2, 1);
        var extractedTo = new DateOnly(2026, 2, 7);
        insightEngine.RangeToReturn = new RequestedRange(extractedFrom, extractedTo);

        // Create an audit logger for the ask
        var auditLogger = new FakeAuditLogger();

        // Create the orchestrator from all components
        var orchestrator = new AskOrchestrator(consentStore, insightEngine, logDataSource, auditLogger);

        var result = await orchestrator.AskAsync("user-1", "how am I doing?");

        // granted is true -> we get an AskAllowed
        var allowed = Assert.IsType<AskAllowed>(result);

        // Compare the fake object and the result through the orchestrator
        Assert.Same(insightEngine.AnswerToReturn, allowed.Answer);
        
        // Confirms that the extracted date is threaded through the orchestrator correctly
        Assert.Equal(("user-1", (DateOnly?)extractedFrom, (DateOnly?)extractedTo), logDataSource.LastRequest);
    }

    [Fact]
    public async Task AskAsync_WhenConsentNotGranted_ReturnsDeniedAndNeverCallsInsightEngine()
    {
        var consentStore = new FakeConsentStore();
        await consentStore.SetConsentAsync("user-1", granted: false);
        var logDataSource = new FakeLogDataSource();
        var insightEngine = new FakeInsightEngine();
        var auditLogger = new FakeAuditLogger();
        var orchestrator = new AskOrchestrator(consentStore, insightEngine, logDataSource, auditLogger);

        var result = await orchestrator.AskAsync("user-1", "how am I doing?");

        // granted is false -> we get an AskDenied
        Assert.IsType<AskDenied>(result);

        // Checking counters are zero to prove that the insight engine wasn't called
        Assert.Equal(0, insightEngine.ExtractRangeCallCount);
        Assert.Equal(0, insightEngine.AskCallCount);
    }

    [Fact]
    public async Task AskAsync_NeverGivesInsightEngineAnythingBeyondTheAggregatedStatsItWasSeeded()
    {
        var consentStore = new FakeConsentStore();
        await consentStore.SetConsentAsync("user-1", granted: true);
        var logDataSource = new FakeLogDataSource();
        var seededStats = SomeStats("user-1");
        logDataSource.Seed("user-1", seededStats);
        var insightEngine = new FakeInsightEngine();
        var auditLogger = new FakeAuditLogger();
        var orchestrator = new AskOrchestrator(consentStore, insightEngine, logDataSource, auditLogger);

        await orchestrator.AskAsync("user-1", "how am I doing?");

        // Checks that no data was fabricated or smuggled in alongside the request
        Assert.Same(seededStats, insightEngine.LastStatsReceived);
    }

    [Fact]
    public async Task AskAsync_UsesTheGivenUserId_NotSomeoneElses()
    {
        var consentStore = new FakeConsentStore();
        await consentStore.SetConsentAsync("user-1", granted: true);
        await consentStore.SetConsentAsync("user-2", granted: true);

        var logDataSource = new FakeLogDataSource();
        logDataSource.Seed("user-1", SomeStats("user-1"));
        var statsForUser2 = SomeStats("user-2");
        logDataSource.Seed("user-2", statsForUser2);

        var insightEngine = new FakeInsightEngine();
        var auditLogger = new FakeAuditLogger();
        var orchestrator = new AskOrchestrator(consentStore, insightEngine, logDataSource, auditLogger);

        // Ask for one of the distinguishable users
        await orchestrator.AskAsync("user-2", "how am I doing?");

        // Confirm that the data belongs to user-2
        Assert.Equal("user-2", logDataSource.LastRequest?.SourceId);

        // Confirm the exact same data was passed on
        Assert.Same(statsForUser2, insightEngine.LastStatsReceived);

        var entry = Assert.Single(auditLogger.Entries); // only user-2 asked
        Assert.Equal("user-2", entry.UserId);
    }

    [Fact]
    public async Task AskAsync_RecordsAnAuditEntry_OnBothTheGrantedAndDeniedPaths()
    {
        var consentStore = new FakeConsentStore();
        await consentStore.SetConsentAsync("user-1", granted: true);

        var logDataSource = new FakeLogDataSource();
        var seededStats = SomeStats("user-1");
        logDataSource.Seed("user-1", seededStats);
        var insightEngine = new FakeInsightEngine();

        var auditLogger = new FakeAuditLogger();

        var orchestrator = new AskOrchestrator(consentStore, insightEngine, logDataSource, auditLogger);
        string question = "how many entries this week?";

        // First call happens while consent is granted...
        await orchestrator.AskAsync("user-1", question);

        // ...then the same user revokes consent before the second call.
        await consentStore.SetConsentAsync("user-1", granted: false);
        await orchestrator.AskAsync("user-1", question);

        Assert.Equal(2, auditLogger.Entries.Count);

        var grantedEntry = auditLogger.Entries[0];
        Assert.Equal("user-1", grantedEntry.UserId);
        Assert.Equal(question, grantedEntry.QueryText);
        Assert.True(grantedEntry.ConsentGranted);
        Assert.Same(seededStats, grantedEntry.AggregatedDataSent);

        var deniedEntry = auditLogger.Entries[1];
        Assert.Equal("user-1", deniedEntry.UserId);
        Assert.Equal(question, deniedEntry.QueryText);
        Assert.False(deniedEntry.ConsentGranted);
        Assert.Null(deniedEntry.AggregatedDataSent);
    }
}
