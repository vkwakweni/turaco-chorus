using System.Threading.RateLimiting;

namespace TuracoChorus.RateLimiting;

/// <summary>
/// Builds the limiter for <c>POST /ask</c>: a per-client-address window and a global daily window, both
/// of which must have room. Every other route gets no limit. The client address is the connection's remote
/// address, which behind the proxy is the real client only when forwarded headers are trusted (see Program.cs).
/// </summary>
internal static class AskRateLimiting
{
    private const string AskPath = "/ask";

    public static PartitionedRateLimiter<HttpContext> Create(AskRateLimitOptions options)
    {
        var perIp = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsAsk(context)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PerIpPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter("not-ask"));

        var global = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsAsk(context)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    "all-clients",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalPerDay,
                        Window = TimeSpan.FromDays(1),
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter("not-ask"));

        return PartitionedRateLimiter.CreateChained(perIp, global);
    }

    private static bool IsAsk(HttpContext context)
        => context.Request.Path.Equals(AskPath, StringComparison.OrdinalIgnoreCase);
}
