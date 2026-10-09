namespace TuracoChorus.RateLimiting;

/// <summary>
/// Limits on <c>POST /ask</c>, the one route that spends on the AI provider. Both are fixed
/// windows held in memory, so they reset when the service restarts (a single instance by design).
/// The defaults are conservative guesses, not derived from any provider's quota: set them from
/// the quota of whichever provider is configured.
/// </summary>
/// <param name="PerIpPerMinute">Requests one client address may make per minute.</param>
/// <param name="GlobalPerDay">Requests all clients together may make per day. Caps the spend when many addresses are used.</param>
public sealed record AskRateLimitOptions(int PerIpPerMinute = 10, int GlobalPerDay = 500);

internal static class AskRateLimitOptionsReader
{
    public static AskRateLimitOptions Read(IConfiguration configuration)
    {
        var defaults = new AskRateLimitOptions();
        return new AskRateLimitOptions(
            PerIpPerMinute: ReadPositive(configuration, "RateLimiting:Ask:PerIpPerMinute", defaults.PerIpPerMinute),
            GlobalPerDay: ReadPositive(configuration, "RateLimiting:Ask:GlobalPerDay", defaults.GlobalPerDay));
    }

    private static int ReadPositive(IConfiguration configuration, string key, int fallback)
    {
        if (configuration[key] is not { Length: > 0 } raw)
        {
            return fallback;
        }

        return int.TryParse(raw, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"Config key \"{key}\" must be a whole number above zero. Got \"{raw}\".");
    }
}
