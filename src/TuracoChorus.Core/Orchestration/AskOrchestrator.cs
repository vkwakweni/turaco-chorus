using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Orchestration;

public sealed class AskOrchestrator(
    IConsentStore consentStore,
    IInsightEngine insightEngine,
    ILogDataSource logDataSource,
    IAuditLogger auditLogger)
{
    public async Task<AskResult> AskAsync(string userId, string question)
    {
        var consent = await consentStore.GetConsentAsync(userId);

        if (!consent.Granted)
        {
            await auditLogger.RecordAuditEntryAsync(new AuditEntry(
                UserId: userId,
                QueryText: question,
                AggregatedDataSent: null,
                ConsentGranted: false,
                Timestamp: DateTimeOffset.UtcNow));

            return new AskDenied();
        }

        var requestedRange = await insightEngine.ExtractRangeAsync(question);
        var stats = await logDataSource.GetStatsAsync(userId, requestedRange.From, requestedRange.To);

        Answer answer;
        try
        {
            answer = await insightEngine.AskAsync(stats, question);
        }
        catch (QuestionNotAnsweredException)
        {
            answer = new Answer(
                Text: "I couldn't determine an answer to that from your data.",
                DataUsed: new DataUsed(StatsQueried: [], Range: stats.Range));
        }

        await auditLogger.RecordAuditEntryAsync(new AuditEntry(
            UserId: userId,
            QueryText: question,
            AggregatedDataSent: stats,
            ConsentGranted: true,
            Timestamp: DateTimeOffset.UtcNow));

        return new AskAllowed(answer);
    }
}
