using TuracoChorus.Adapters.Claude;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

internal static class ClaudeInsightEngineOptionsReader
{
    public static ClaudeInsightEngineOptions Read(IConfiguration configuration)
    {
        var options = new ClaudeInsightEngineOptions(ApiKey: RequireString(configuration, "Claude:ApiKey"));

        if (configuration["Claude:Model"] is { Length: > 0 } model)
        {
            options = options with { Model = model };
        }

        if (configuration["Claude:BaseUrl"] is { Length: > 0 } baseUrl)
        {
            options = options with { BaseUrl = baseUrl };
        }

        return options;
    }
}
