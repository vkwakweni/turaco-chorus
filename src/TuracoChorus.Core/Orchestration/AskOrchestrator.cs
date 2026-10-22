using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Orchestration;

public sealed class AskOrchestrator(
    IConsentStore consentStore,
    IInsightEngine insightEngine,
    ILogDataSource logDataSource,
    IAuditLogger auditLogger,
    ILogger<AskOrchestrator>? logger = null)
{
    private readonly ILogger<AskOrchestrator> _logger = logger ?? NullLogger<AskOrchestrator>.Instance;

    public async Task<AskResult> AskAsync(string userId, string question)
    {
        ConsentRecord consent;
        try
        {
            consent = await consentStore.GetConsentAsync(userId);
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(userId, question, consentGranted: null, stats: null, ex);
            throw;
        }

        if (!consent.Granted)
        {
            await auditLogger.RecordAuditEntryAsync(new AuditEntry(
                UserId: userId,
                QueryText: question,
                AggregatedDataSent: null,
                ConsentGranted: false,
                Outcome: AuditOutcome.Denied,
                Timestamp: DateTimeOffset.UtcNow));

            return new AskDenied();
        }

        AggregateStats? stats = null;
        Answer answer;
        var outcome = AuditOutcome.Answered;
        try
        {
            var requestedRange = await insightEngine.ExtractRangeAsync(question);
            stats = await logDataSource.GetStatsAsync(userId, requestedRange.From, requestedRange.To);

            try
            {
                answer = await insightEngine.AskAsync(stats, question);
            }
            catch (QuestionNotAnsweredException)
            {
                outcome = AuditOutcome.CouldNotAnswer;
                answer = new Answer(
                    Text: "I couldn't determine an answer to that from your data.",
                    DataUsed: new DataUsed(StatsQueried: [], Range: stats.Range));
            }
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(userId, question, consentGranted: true, stats, ex);
            throw;
        }

        await auditLogger.RecordAuditEntryAsync(new AuditEntry(
            UserId: userId,
            QueryText: question,
            AggregatedDataSent: stats,
            ConsentGranted: true,
            Outcome: outcome,
            Timestamp: DateTimeOffset.UtcNow));

        return new AskAllowed(answer);
    }

    // Records the attempt before the caller rethrows. A failing audit write is logged and swallowed so
    // it can never replace the exception that actually ended the request.
    private async Task RecordFailureAsync(
        string userId,
        string question,
        bool? consentGranted,
        AggregateStats? stats,
        Exception cause)
    {
        try
        {
            await auditLogger.RecordAuditEntryAsync(new AuditEntry(
                UserId: userId,
                QueryText: question,
                AggregatedDataSent: stats,
                ConsentGranted: consentGranted,
                Outcome: AuditOutcome.Failed,
                Timestamp: DateTimeOffset.UtcNow));
        }
        catch (Exception auditFailure)
        {
            _logger.LogError(
                auditFailure,
                "Could not record the audit entry for a failed /ask ({CauseType}).",
                cause.GetType().Name);
        }
    }
}
