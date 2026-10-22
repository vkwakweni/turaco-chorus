namespace TuracoChorus.Adapters.Gemini.Tests;

/// <summary>
/// One real call to the Google Gemini API (`generateContent`) — no fake HTTP handler. Excluded
/// from the default test run (this project's own `VSTestTestCaseFilter` keeps a plain
/// `dotnet test` green without credentials/spend); run with `dotnet test --filter "Category=Integration"`.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GeminiInsightEngineIntegrationTests
{
    [Fact]
    public async Task ExtractRangeAsync_AgainstRealApi_ReturnsWithoutThrowing()
    {
        var options = new GeminiInsightEngineOptions(ApiKey: RequireEnv("TURACO_TEST_GEMINI_API_KEY"));
        using var httpClient = new HttpClient();
        var engine = new GeminiInsightEngine(options, httpClient);

        var range = await engine.ExtractRangeAsync("How many entries did I log in January 2026?");

        Assert.NotNull(range);
    }

    private static string RequireEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Integration test requires environment variable \"{name}\" to be set.");
}
