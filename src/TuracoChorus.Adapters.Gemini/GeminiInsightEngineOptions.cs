namespace TuracoChorus.Adapters.Gemini;

public sealed record GeminiInsightEngineOptions(
    string ApiKey,
    string Model = "gemini-3.6-flash",
    string BaseUrl = "https://generativelanguage.googleapis.com");
