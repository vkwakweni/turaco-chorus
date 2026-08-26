using Microsoft.Extensions.Configuration;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class DynamoDbConsentStoreOptionsReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Read_ReturnsOptions_WhenTableNamePresent()
    {
        var config = Build(new() { ["DynamoDb:Consent:TableName"] = "TuracoChorusConsent" });

        var options = DynamoDbConsentStoreOptionsReader.Read(config);

        Assert.Equal("TuracoChorusConsent", options.TableName);
    }

    [Fact]
    public void Read_Throws_NamingTheMissingKey()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbConsentStoreOptionsReader.Read(config));
        Assert.Contains("DynamoDb:Consent:TableName", ex.Message);
    }
}
