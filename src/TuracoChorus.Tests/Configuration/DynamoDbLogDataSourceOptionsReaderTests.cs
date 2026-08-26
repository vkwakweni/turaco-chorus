using Microsoft.Extensions.Configuration;
using TuracoChorus.Adapters.DynamoDb;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class DynamoDbLogDataSourceOptionsReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["DynamoDb:LogData:TableName"] = "MyTable",
        ["DynamoDb:LogData:PartitionKeyAttribute"] = "PK",
        ["DynamoDb:LogData:PartitionKeyValueTemplate"] = "USER#{sourceId}",
        ["DynamoDb:LogData:SortKeyAttribute"] = "SK",
        ["DynamoDb:LogData:EntrySortKeyPrefix"] = "ENTRY#",
        ["DynamoDb:LogData:DateAttribute"] = "createdAt",
        ["DynamoDb:LogData:Dimensions:0:Name"] = "category",
        ["DynamoDb:LogData:Dimensions:0:Type"] = "Lookup",
        ["DynamoDb:LogData:Dimensions:0:IdAttributeName"] = "typeId",
        ["DynamoDb:LogData:Dimensions:0:LookupPartitionKeyValueTemplate"] = "USER#{sourceId}",
        ["DynamoDb:LogData:Dimensions:0:LookupSortKeyValueTemplate"] = "TYPE#{typeId}",
        ["DynamoDb:LogData:Dimensions:0:LookupNameAttribute"] = "name",
        ["DynamoDb:LogData:Dimensions:1:Name"] = "date",
        ["DynamoDb:LogData:Dimensions:1:Type"] = "Direct",
        ["DynamoDb:LogData:Dimensions:1:AttributeName"] = "createdAt",
    };

    [Fact]
    public void Read_ReturnsOptions_WithBothDimensionShapes()
    {
        var options = DynamoDbLogDataSourceOptionsReader.Read(Build(ValidValues()));

        Assert.Equal("MyTable", options.TableName);
        Assert.Equal("PK", options.PartitionKeyAttribute);
        Assert.Equal("USER#{sourceId}", options.PartitionKeyValueTemplate);
        Assert.Equal("SK", options.SortKeyAttribute);
        Assert.Equal("ENTRY#", options.EntrySortKeyPrefix);
        Assert.Equal("createdAt", options.DateAttribute);
        Assert.Equal(2, options.Dimensions.Count);

        var category = options.Dimensions[0];
        Assert.Equal("category", category.Name);
        var lookup = Assert.IsType<LookupSource>(category.Source);
        Assert.Equal("typeId", lookup.IdAttributeName);
        Assert.Equal("USER#{sourceId}", lookup.LookupPartitionKeyValueTemplate);
        Assert.Equal("TYPE#{typeId}", lookup.LookupSortKeyValueTemplate);
        Assert.Equal("name", lookup.LookupNameAttribute);
        Assert.Null(lookup.LookupTableName);

        var date = options.Dimensions[1];
        Assert.Equal("date", date.Name);
        var direct = Assert.IsType<DirectAttributeSource>(date.Source);
        Assert.Equal("createdAt", direct.AttributeName);
    }

    [Fact]
    public void Read_Throws_NamingTheMissingKey_WhenTableNameMissing()
    {
        var values = ValidValues();
        values.Remove("DynamoDb:LogData:TableName");

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbLogDataSourceOptionsReader.Read(Build(values)));
        Assert.Contains("DynamoDb:LogData:TableName", ex.Message);
    }

    [Fact]
    public void Read_ReturnsEmptyDimensions_WhenSectionAbsent()
    {
        var values = ValidValues();
        values.Remove("DynamoDb:LogData:Dimensions:0:Name");
        values.Remove("DynamoDb:LogData:Dimensions:0:Type");
        values.Remove("DynamoDb:LogData:Dimensions:0:IdAttributeName");
        values.Remove("DynamoDb:LogData:Dimensions:0:LookupPartitionKeyValueTemplate");
        values.Remove("DynamoDb:LogData:Dimensions:0:LookupSortKeyValueTemplate");
        values.Remove("DynamoDb:LogData:Dimensions:0:LookupNameAttribute");
        values.Remove("DynamoDb:LogData:Dimensions:1:Name");
        values.Remove("DynamoDb:LogData:Dimensions:1:Type");
        values.Remove("DynamoDb:LogData:Dimensions:1:AttributeName");

        var options = DynamoDbLogDataSourceOptionsReader.Read(Build(values));

        Assert.Empty(options.Dimensions);
    }

    [Fact]
    public void Read_AllowsOmittedSortKeyAndEntryPrefix()
    {
        var values = ValidValues();
        values.Remove("DynamoDb:LogData:SortKeyAttribute");
        values.Remove("DynamoDb:LogData:EntrySortKeyPrefix");

        var options = DynamoDbLogDataSourceOptionsReader.Read(Build(values));

        Assert.Null(options.SortKeyAttribute);
        Assert.Null(options.EntrySortKeyPrefix);
    }

    [Fact]
    public void Read_Throws_WhenDimensionMissingName()
    {
        var values = ValidValues();
        values.Remove("DynamoDb:LogData:Dimensions:0:Name");

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbLogDataSourceOptionsReader.Read(Build(values)));
        Assert.Contains("DynamoDb:LogData:Dimensions:0:Name", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenDimensionTypeUnrecognized()
    {
        var values = ValidValues();
        values["DynamoDb:LogData:Dimensions:1:Type"] = "Weird";

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbLogDataSourceOptionsReader.Read(Build(values)));
        Assert.Contains("DynamoDb:LogData:Dimensions:1", ex.Message);
        Assert.Contains("Weird", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenLookupMissingIdAttributeName()
    {
        var values = ValidValues();
        values.Remove("DynamoDb:LogData:Dimensions:0:IdAttributeName");

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbLogDataSourceOptionsReader.Read(Build(values)));
        Assert.Contains("DynamoDb:LogData:Dimensions:0", ex.Message);
        Assert.Contains("IdAttributeName", ex.Message);
    }

    [Fact]
    public void Read_Throws_WhenDirectMissingAttributeName()
    {
        var values = ValidValues();
        values.Remove("DynamoDb:LogData:Dimensions:1:AttributeName");

        var ex = Assert.Throws<InvalidOperationException>(() => DynamoDbLogDataSourceOptionsReader.Read(Build(values)));
        Assert.Contains("DynamoDb:LogData:Dimensions:1", ex.Message);
        Assert.Contains("AttributeName", ex.Message);
    }
}
