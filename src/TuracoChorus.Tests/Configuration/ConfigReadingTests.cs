using Microsoft.Extensions.Configuration;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class ConfigReadingTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void RequireString_ReturnsValue_WhenPresent()
    {
        var config = Build(new() { ["Foo"] = "bar" });

        Assert.Equal("bar", ConfigReading.RequireString(config, "Foo"));
    }

    [Fact]
    public void RequireString_Throws_NamingTheKey_WhenMissing()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigReading.RequireString(config, "Foo"));
        Assert.Contains("\"Foo\"", ex.Message);
    }

    private enum Color { Red, Blue }

    [Fact]
    public void RequireEnum_ParsesCaseInsensitively()
    {
        var config = Build(new() { ["Color"] = "red" });

        Assert.Equal(Color.Red, ConfigReading.RequireEnum<Color>(config, "Color"));
    }

    [Fact]
    public void RequireEnum_Throws_WhenMissing()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigReading.RequireEnum<Color>(config, "Color"));
        Assert.Contains("\"Color\"", ex.Message);
    }

    [Fact]
    public void RequireEnum_Throws_ListingValidValues_WhenInvalid()
    {
        var config = Build(new() { ["Color"] = "Green" });

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigReading.RequireEnum<Color>(config, "Color"));
        Assert.Contains("Red", ex.Message);
        Assert.Contains("Blue", ex.Message);
    }
}
