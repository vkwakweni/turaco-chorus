using TuracoChorus.Core.Models;
using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Ports;
using Xunit;

namespace TuracoChorus.Core.Tests.Orchestration;

public sealed class AskOrchestratorTests
{
    private static AggregateStats SomeStats(string sourceId) => new(
        SourceId: sourceId,
        Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
        TotalEntries: 3,
        Dimensions:
        [
            new Dimension("category", [new DimensionBucket("mood", 3)]),
            new Dimension("date", [new DimensionBucket("2026-01-10", 3)])
        ]);

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
        Assert.Equal(AuditOutcome.Answered, grantedEntry.Outcome);
        Assert.Same(seededStats, grantedEntry.AggregatedDataSent);

        var deniedEntry = auditLogger.Entries[1];
        Assert.Equal("user-1", deniedEntry.UserId);
        Assert.Equal(question, deniedEntry.QueryText);
        Assert.False(deniedEntry.ConsentGranted);
        Assert.Equal(AuditOutcome.Denied, deniedEntry.Outcome);
        Assert.Null(deniedEntry.AggregatedDataSent);
    }

    [Fact]
    public async Task AskAsync_WhenInsightEngineCannotAnswer_ReturnsAllowedWithAGracefulAnswer_AndStillAudits()
    {
        var consentStore = new FakeConsentStore();
        await consentStore.SetConsentAsync("user-1", granted: true);

        var logDataSource = new FakeLogDataSource();
        var seededStats = SomeStats("user-1");
        logDataSource.Seed("user-1", seededStats);

        var insightEngine = new FakeInsightEngine
        {
            AskExceptionToThrow = new QuestionNotAnsweredException("Gemini determined this question can't be answered from the available aggregates.")
        };
        var auditLogger = new FakeAuditLogger();
        var orchestrator = new AskOrchestrator(consentStore, insightEngine, logDataSource, auditLogger);

        var result = await orchestrator.AskAsync("user-1", "how am I doing?");

        // The caller asked a question and gets a (negative) answer back, not an error.
        var allowed = Assert.IsType<AskAllowed>(result);
        Assert.False(string.IsNullOrWhiteSpace(allowed.Answer.Text));

        // Ethics-by-Design: every /ask call is audited, regardless of outcome — including this one.
        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("user-1", entry.UserId);
        Assert.True(entry.ConsentGranted);
        Assert.Equal(AuditOutcome.CouldNotAnswer, entry.Outcome);
        Assert.Same(seededStats, entry.AggregatedDataSent);
    }

    private sealed record Rig(
        FakeConsentStore Consent,
        FakeLogDataSource Data,
        FakeInsightEngine Engine,
        FakeAuditLogger Audit,
        AggregateStats Stats,
        AskOrchestrator Orchestrator);

    // A user who has granted consent and has seeded stats; each test then breaks one collaborator.
    private static async Task<Rig> GrantedRig()
    {
        var consent = new FakeConsentStore();
        await consent.SetConsentAsync("user-1", granted: true);
        var data = new FakeLogDataSource();
        var stats = SomeStats("user-1");
        data.Seed("user-1", stats);
        var engine = new FakeInsightEngine();
        var audit = new FakeAuditLogger();
        return new Rig(consent, data, engine, audit, stats, new AskOrchestrator(consent, engine, data, audit));
    }

    private static void AssertSingleFailedEntry(Rig rig, string question, bool? consentGranted, AggregateStats? stats)
    {
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal(1, rig.Audit.AttemptCount);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal(question, entry.QueryText);
        Assert.Equal(consentGranted, entry.ConsentGranted);
        Assert.Same(stats, entry.AggregatedDataSent);
        Assert.NotEqual(default, entry.Timestamp);
    }

    [Fact]
    public async Task AskAsync_WhenTheConsentLookupThrows_AuditsFailedWithUnknownConsent_AndRethrowsTheOriginal()
    {
        var rig = await GrantedRig();
        var boom = new InvalidOperationException("consent store down");
        rig.Consent.GetExceptionToThrow = boom;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(boom, thrown);
        AssertSingleFailedEntry(rig, "q", consentGranted: null, stats: null);
        Assert.Equal(0, rig.Engine.ExtractRangeCallCount);
    }

    [Fact]
    public async Task AskAsync_WhenRangeExtractionThrows_AuditsFailedWithNoData_AndRethrowsTheOriginal()
    {
        var rig = await GrantedRig();
        var boom = new HttpRequestException("provider 503");
        rig.Engine.ExtractRangeExceptionToThrow = boom;

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(boom, thrown);
        AssertSingleFailedEntry(rig, "q", consentGranted: true, stats: null);
        Assert.Equal(0, rig.Engine.AskCallCount);
    }

    [Fact]
    public async Task AskAsync_WhenTheDataSourceThrows_AuditsFailedWithNoData_AndNeverCallsTheEngineToAnswer()
    {
        var rig = await GrantedRig();
        var boom = new InvalidOperationException("table unreachable");
        rig.Data.ExceptionToThrow = boom;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(boom, thrown);
        AssertSingleFailedEntry(rig, "q", consentGranted: true, stats: null);
        Assert.Equal(0, rig.Engine.AskCallCount);
        Assert.Null(rig.Engine.LastStatsReceived);
    }

    [Fact]
    public async Task AskAsync_WhenTheAnswerStepThrowsAnythingButQuestionNotAnswered_AuditsFailedWithTheStatsSent()
    {
        var rig = await GrantedRig();
        var boom = new InsightResponseParseException("unparseable");
        rig.Engine.AskExceptionToThrow = boom;

        var thrown = await Assert.ThrowsAsync<InsightResponseParseException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(boom, thrown);
        // The stats had already gone to the provider, so the entry must say so.
        AssertSingleFailedEntry(rig, "q", consentGranted: true, stats: rig.Stats);
    }

    [Fact]
    public async Task AskAsync_WhenTheFailurePathAuditWriteAlsoThrows_StillRethrowsTheOriginalException_OnceAttempted()
    {
        var rig = await GrantedRig();
        var original = new InvalidOperationException("data source down");
        rig.Data.ExceptionToThrow = original;
        rig.Audit.ExceptionToThrow = new TimeoutException("audit table down");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(original, thrown);
        Assert.Equal(1, rig.Audit.AttemptCount);
    }

    [Fact]
    public async Task AskAsync_WhenTheConsentLookupAndTheFailureAuditBothThrow_RethrowsTheConsentException()
    {
        var rig = await GrantedRig();
        var original = new InvalidOperationException("consent store down");
        rig.Consent.GetExceptionToThrow = original;
        rig.Audit.ExceptionToThrow = new TimeoutException("audit table down");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(original, thrown);
        Assert.Equal(1, rig.Audit.AttemptCount);
    }

    [Fact]
    public async Task AskAsync_WhenTheFailureAuditWriteThrows_LogsIt()
    {
        var consent = new FakeConsentStore();
        await consent.SetConsentAsync("user-1", granted: true);
        var data = new FakeLogDataSource { ExceptionToThrow = new InvalidOperationException("data source down") };
        var audit = new FakeAuditLogger { ExceptionToThrow = new TimeoutException("audit table down") };
        var logger = new RecordingLogger<AskOrchestrator>();
        var orchestrator = new AskOrchestrator(consent, new FakeInsightEngine(), data, audit, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.AskAsync("user-1", "q"));

        var logged = Assert.Single(logger.Errors);
        Assert.IsType<TimeoutException>(logged.Exception);
    }

    [Fact]
    public async Task AskAsync_WhenTheSuccessPathAuditWriteThrows_PropagatesIt_WritesOnce_AndDoesNotReAuditAsFailed()
    {
        var rig = await GrantedRig();
        var auditFailure = new TimeoutException("audit table down");
        rig.Audit.ExceptionToThrow = auditFailure;

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(auditFailure, thrown);
        Assert.Equal(1, rig.Audit.AttemptCount);
        Assert.Empty(rig.Audit.Entries);
    }

    [Fact]
    public async Task AskAsync_WhenTheDeniedPathAuditWriteThrows_PropagatesIt_AndWritesOnce()
    {
        var rig = await GrantedRig();
        await rig.Consent.SetConsentAsync("user-1", granted: false);
        var auditFailure = new TimeoutException("audit table down");
        rig.Audit.ExceptionToThrow = auditFailure;

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => rig.Orchestrator.AskAsync("user-1", "q"));

        Assert.Same(auditFailure, thrown);
        Assert.Equal(1, rig.Audit.AttemptCount);
        Assert.Equal(0, rig.Engine.ExtractRangeCallCount);
    }

    [Fact]
    public async Task AskAsync_WritesExactlyOneAuditEntry_OnEveryPath()
    {
        // Answered
        var answered = await GrantedRig();
        await answered.Orchestrator.AskAsync("user-1", "q");
        Assert.Equal(1, answered.Audit.AttemptCount);
        Assert.Equal(AuditOutcome.Answered, Assert.Single(answered.Audit.Entries).Outcome);

        // Denied
        var denied = await GrantedRig();
        await denied.Consent.SetConsentAsync("user-1", granted: false);
        await denied.Orchestrator.AskAsync("user-1", "q");
        Assert.Equal(1, denied.Audit.AttemptCount);
        Assert.Equal(AuditOutcome.Denied, Assert.Single(denied.Audit.Entries).Outcome);

        // Could not answer
        var cannotAnswer = await GrantedRig();
        cannotAnswer.Engine.AskExceptionToThrow = new QuestionNotAnsweredException("no");
        await cannotAnswer.Orchestrator.AskAsync("user-1", "q");
        Assert.Equal(1, cannotAnswer.Audit.AttemptCount);
        Assert.Equal(AuditOutcome.CouldNotAnswer, Assert.Single(cannotAnswer.Audit.Entries).Outcome);
    }

    [Fact]
    public async Task AskAsync_WhenTheQuestionCannotBeAnswered_IsNotAuditedAsFailed_AndDoesNotThrow()
    {
        var rig = await GrantedRig();
        rig.Engine.AskExceptionToThrow = new QuestionNotAnsweredException("no");

        var result = await rig.Orchestrator.AskAsync("user-1", "q");

        Assert.IsType<AskAllowed>(result);
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.NotEqual(AuditOutcome.Failed, entry.Outcome);
        Assert.Same(rig.Stats, entry.AggregatedDataSent);
    }

    [Fact]
    public async Task AskAsync_WhenConsentIsDenied_NeverCallsTheEngineOrTheDataSource()
    {
        var rig = await GrantedRig();
        await rig.Consent.SetConsentAsync("user-1", granted: false);

        await rig.Orchestrator.AskAsync("user-1", "q");

        Assert.Equal(0, rig.Engine.ExtractRangeCallCount);
        Assert.Equal(0, rig.Engine.AskCallCount);
        Assert.Null(rig.Data.LastRequest);
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(Exception? Exception, string Message)> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error)
            {
                Errors.Add((exception, formatter(state, exception)));
            }
        }
    }
}
