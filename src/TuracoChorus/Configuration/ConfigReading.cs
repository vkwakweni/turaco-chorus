namespace TuracoChorus.Configuration;

/// <summary>
/// Shared fail-fast helpers for every adapter's config reader: read a required value from
/// <see cref="IConfiguration"/>, or throw immediately naming the exact key that's missing/invalid.
/// </summary>
internal static class ConfigReading
{
    public static string RequireString(IConfiguration configuration, string key)
        => configuration[key] is { Length: > 0 } value ? value : throw Missing(key);

    public static TEnum RequireEnum<TEnum>(IConfiguration configuration, string key) where TEnum : struct, Enum
    {
        var raw = RequireString(configuration, key);
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Config key \"{key}\" must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}. Got \"{raw}\".");
    }

    public static InvalidOperationException Missing(string key) => new(
        $"Missing required configuration \"{key}\". Set it via `dotnet user-secrets set \"{key}\" \"<value>\"` " +
        "for local development, or your deployment's own secrets manager/environment variables — or set " +
        "\"UseFakeAdapters\": \"true\" to run against in-memory fakes instead.");
}
