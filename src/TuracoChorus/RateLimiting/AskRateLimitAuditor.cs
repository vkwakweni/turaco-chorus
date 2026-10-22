using System.Text.Json;
using TuracoChorus.Auth;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.RateLimiting;

/// <summary>
/// Audits a <c>POST /ask</c> that the rate limiter rejected. The limiter runs ahead of authentication, so
/// this authenticates the caller itself: a rejection is audited only when it carries a valid credential,
/// because an unauthenticated rejection has no user to attribute it to and nothing could have been sent to
/// the AI provider. There is no de-duplication: each rejected request from a valid caller writes one entry,
/// bounded only by the per-address limit (see artifacts/ethics-by-design.md's known limitations).
/// It never throws, so the caller still receives the 429.
/// </summary>
internal sealed class AskRateLimitAuditor(
    IIdentityVerifier identityVerifier,
    IAuditLogger auditLogger,
    TimeProvider timeProvider,
    ILogger<AskRateLimitAuditor> logger)
{
    /// <summary>Largest request body read to recover the question; anything bigger is recorded without one.</summary>
    internal const int MaxBodyBytes = 64 * 1024;

    public async Task RecordAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await BearerAuth.AuthenticateAsync(context.Request, identityVerifier);
            if (auth is not AuthSucceeded { UserId: var userId })
            {
                return;
            }

            var question = await ReadQuestionAsync(context.Request, cancellationToken);

            await auditLogger.RecordAuditEntryAsync(new AuditEntry(
                UserId: userId,
                QueryText: question,
                AggregatedDataSent: null,
                ConsentGranted: null,
                Outcome: AuditOutcome.RateLimited,
                Timestamp: timeProvider.GetUtcNow()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record the audit entry for a rate-limited /ask.");
        }
    }

    // The attempt is what must be recorded, so a missing, oversized or malformed body yields an empty question
    // rather than skipping the entry.
    private static async Task<string> ReadQuestionAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return string.Empty;
                }

                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0)
            {
                return string.Empty;
            }

            buffer.Position = 0;
            using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("question", StringComparison.OrdinalIgnoreCase))
                    {
                        return property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : string.Empty;
                    }
                }
            }

            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
