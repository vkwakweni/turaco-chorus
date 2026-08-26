using Microsoft.Extensions.Configuration;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class DynamoDbAskAuditLoggerOptionsReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Read_ReturnsOptions_WhenTableNamePresent()
    {
        var config = Build(new() { ["DynamoDb:Audit:TableName"] = "TuracoChorusAskAudit" });

        var options = DynamoDbAskAuditLoggerOptionsReader.Read(config);

        Assert.Equal("TuracoChorusAskAudit", options.TableName);
    }

    [Fact]
    public void Read_Throws_NamingTheMissingKey()
    {
        var config = Build(new());

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbAskAuditLoggerOptionsReader.Read(config));
        Assert.Contains("DynamoDb:Audit:TableName", ex.Message);
    }
}
