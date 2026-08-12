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
        var answer = await insightEngine.AskAsync(stats, question);

        await auditLogger.RecordAuditEntryAsync(new AuditEntry(
            UserId: userId,
            QueryText: question,
            AggregatedDataSent: stats,
            ConsentGranted: true,
            Timestamp: DateTimeOffset.UtcNow));

        return new AskAllowed(answer);
    }
}
