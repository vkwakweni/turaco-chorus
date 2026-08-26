using TuracoChorus.Adapters.Gemini;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

internal static class GeminiInsightEngineOptionsReader
{
    public static GeminiInsightEngineOptions Read(IConfiguration configuration)
    {
        var options = new GeminiInsightEngineOptions(ApiKey: RequireString(configuration, "Gemini:ApiKey"));

        if (configuration["Gemini:Model"] is { Length: > 0 } model)
        {
            options = options with { Model = model };
        }

        if (configuration["Gemini:BaseUrl"] is { Length: > 0 } baseUrl)
        {
            options = options with { BaseUrl = baseUrl };
        }

        return options;
    }
}
