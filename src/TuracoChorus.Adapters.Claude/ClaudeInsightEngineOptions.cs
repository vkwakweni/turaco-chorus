namespace TuracoChorus.Adapters.Claude;

public sealed record ClaudeInsightEngineOptions(
    string ApiKey,
    string Model = "claude-haiku-4-5",
    string BaseUrl = "https://api.anthropic.com");
