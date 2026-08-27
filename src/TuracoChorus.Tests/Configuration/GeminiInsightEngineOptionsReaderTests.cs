using Microsoft.Extensions.Configuration;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class GeminiInsightEngineOptionsReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Read_ReturnsOptions_WhenAllKeysPresent()
    {
        var config = Build(new()
        {
            ["Gemini:ApiKey"] = "key-456",
            ["Gemini:Model"] = "gemini-custom",
            ["Gemini:BaseUrl"] = "https://example.test",
        });

        var options = GeminiInsightEngineOptionsReader.Read(config);

        Assert.Equal("key-456", options.ApiKey);
        Assert.Equal("gemini-custom", options.Model);
        Assert.Equal("https://example.test", options.BaseUrl);
    }

    [Fact]
    public void Read_FallsBackToAdapterDefaults_WhenModelAndBaseUrlOmitted()
    {
        var config = Build(new() { ["Gemini:ApiKey"] = "key-456" });

        var options = GeminiInsightEngineOptionsReader.Read(config);

        Assert.Equal("gemini-3.6-flash", options.Model);
        Assert.Equal("https://generativelanguage.googleapis.com", options.BaseUrl);
    }

    [Fact]
    public void Read_Throws_NamingTheMissingKey()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => GeminiInsightEngineOptionsReader.Read(config));
        Assert.Contains("Gemini:ApiKey", ex.Message);
    }
}
