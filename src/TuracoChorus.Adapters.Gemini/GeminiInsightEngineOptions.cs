namespace TuracoChorus.Adapters.Gemini;

public sealed record GeminiInsightEngineOptions(
    string ApiKey,
    string Model = "gemini-2.5-flash",
    string BaseUrl = "https://generativelanguage.googleapis.com");
