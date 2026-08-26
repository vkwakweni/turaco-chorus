using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

/// <summary>Which IInsightEngine adapter gets registered — a host-level choice, not any one adapter's concern.</summary>
public enum InsightProvider
{
    Claude,
    Gemini
}

internal static class InsightProviderReader
{
    public static InsightProvider Read(IConfiguration configuration)
        => RequireEnum<InsightProvider>(configuration, "InsightProvider");
}
