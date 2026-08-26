using Microsoft.Extensions.Configuration;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class ClaudeInsightEngineOptionsReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Read_ReturnsOptions_WhenAllKeysPresent()
    {
        var config = Build(new()
        {
            ["Claude:ApiKey"] = "key-123",
            ["Claude:Model"] = "claude-custom",
            ["Claude:BaseUrl"] = "https://example.test",
        });

        var options = ClaudeInsightEngineOptionsReader.Read(config);

        Assert.Equal("key-123", options.ApiKey);
        Assert.Equal("claude-custom", options.Model);
        Assert.Equal("https://example.test", options.BaseUrl);
    }

    [Fact]
    public void Read_FallsBackToAdapterDefaults_WhenModelAndBaseUrlOmitted()
    {
        var config = Build(new() { ["Claude:ApiKey"] = "key-123" });

        var options = ClaudeInsightEngineOptionsReader.Read(config);

        Assert.Equal("claude-haiku-4-5", options.Model);
        Assert.Equal("https://api.anthropic.com", options.BaseUrl);
    }

    [Fact]
    public void Read_Throws_NamingTheMissingKey()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => ClaudeInsightEngineOptionsReader.Read(config));
        Assert.Contains("Claude:ApiKey", ex.Message);
    }
}
