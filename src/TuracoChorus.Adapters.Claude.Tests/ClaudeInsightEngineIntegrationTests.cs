namespace TuracoChorus.Adapters.Claude.Tests;

/// <summary>
/// One real call to the Anthropic Messages API — no fake HTTP handler. Excluded from the default
/// test run (this project's own `VSTestTestCaseFilter` keeps a plain `dotnet test` green without
/// credentials/spend); run with `dotnet test --filter "Category=Integration"`.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ClaudeInsightEngineIntegrationTests
{
    [Fact]
    public async Task ExtractRangeAsync_AgainstRealApi_ReturnsWithoutThrowing()
    {
        var options = new ClaudeInsightEngineOptions(ApiKey: RequireEnv("TURACO_TEST_CLAUDE_API_KEY"));
        using var httpClient = new HttpClient();
        var engine = new ClaudeInsightEngine(options, httpClient);

        var range = await engine.ExtractRangeAsync("How many entries did I log in January 2026?");

        Assert.NotNull(range);
    }

    private static string RequireEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Integration test requires environment variable \"{name}\" to be set.");
}
