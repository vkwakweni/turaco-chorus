using Microsoft.Extensions.Configuration;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class InsightProviderReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData("Claude", InsightProvider.Claude)]
    [InlineData("Gemini", InsightProvider.Gemini)]
    [InlineData("gemini", InsightProvider.Gemini)]
    public void Read_ParsesValidValues(string raw, InsightProvider expected)
    {
        var config = Build(new() { ["InsightProvider"] = raw });

        Assert.Equal(expected, InsightProviderReader.Read(config));
    }

    [Fact]
    public void Read_Throws_NamingTheKey_WhenMissing()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => InsightProviderReader.Read(config));
        Assert.Contains("InsightProvider", ex.Message);
    }

    [Fact]
    public void Read_Throws_ListingValidValues_WhenInvalid()
    {
        var config = Build(new() { ["InsightProvider"] = "OpenAi" });

        var ex = Assert.Throws<InvalidOperationException>(() => InsightProviderReader.Read(config));
        Assert.Contains("Claude", ex.Message);
        Assert.Contains("Gemini", ex.Message);
    }
}
