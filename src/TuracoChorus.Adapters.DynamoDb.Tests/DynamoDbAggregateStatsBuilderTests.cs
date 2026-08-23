using Amazon.DynamoDBv2.Model;

namespace TuracoChorus.Adapters.DynamoDb.Tests;

public sealed class DynamoDbAggregateStatsBuilderTests
{
    private const string SourceId = "user-1";

    // A synthetic single-table layout: a colocated Lookup dimension ("category") and a
    // DirectAttribute dimension ("date") — not modelled on any specific installer's schema.
    private static readonly DynamoDbLogDataSourceOptions SampleOptions = new(
        TableName: "SourceTable",
        Region: "us-east-1",
        PartitionKeyAttribute: "PK",
        PartitionKeyValueTemplate: "USER#{sourceId}",
        SortKeyAttribute: "SK",
        EntrySortKeyPrefix: "ENTRY#",
        DateAttribute: "createdAt",
        Dimensions:
        [
            new DimensionConfig("category", new LookupSource(
                IdAttributeName: "categoryId",
                LookupTableName: null,
                LookupPartitionKeyValueTemplate: "USER#{sourceId}",
                LookupSortKeyValueTemplate: "CAT#{categoryId}",
                LookupNameAttribute: "name")),
            new DimensionConfig("date", new DirectAttributeSource(AttributeName: "createdAt")),
        ]);

    private static Dictionary<string, AttributeValue> EntryItem(string categoryId, string createdAt) => new()
    {
        ["PK"] = new AttributeValue { S = $"USER#{SourceId}" },
        ["SK"] = new AttributeValue { S = $"ENTRY#{categoryId}#{createdAt}" },
        ["categoryId"] = new AttributeValue { S = categoryId },
        ["createdAt"] = new AttributeValue { S = createdAt },
    };

    private static Dictionary<string, AttributeValue> CategoryItem(string categoryId, string name) => new()
    {
        ["PK"] = new AttributeValue { S = $"USER#{SourceId}" },
        ["SK"] = new AttributeValue { S = $"CAT#{categoryId}" },
        ["name"] = new AttributeValue { S = name },
    };

    private static readonly List<Dictionary<string, AttributeValue>> ThreeEntriesAndTwoCategories =
    [
        EntryItem("cat-a", "2026-01-01T10:00:00Z"),
        EntryItem("cat-b", "2026-01-03T09:00:00Z"),
        EntryItem("cat-a", "2026-01-05T09:00:00Z"),
        CategoryItem("cat-a", "Category A"),
        CategoryItem("cat-b", "Category B"),
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NoFallbackLookups =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    [Fact]
    public void Build_ColocatedLookupAndDirectAttribute_ResolvesBothDimensionsAndCounts()
    {
        var stats = DynamoDbAggregateStatsBuilder.Build(
            SourceId, SampleOptions, ThreeEntriesAndTwoCategories, from: null, to: null, NoFallbackLookups);

        Assert.Equal(3, stats.TotalEntries);

        var category = Assert.Single(stats.Dimensions, d => d.Name == "category");
        Assert.Equal(2, category.Buckets.Single(b => b.Value == "Category A").Count);
        Assert.Equal(1, category.Buckets.Single(b => b.Value == "Category B").Count);

        var date = Assert.Single(stats.Dimensions, d => d.Name == "date");
        Assert.Equal(3, date.Buckets.Count);
        Assert.All(date.Buckets, b => Assert.Equal(1, b.Count));
    }

    [Fact]
    public void Build_WithDateRangeBounds_ExcludesEntriesOutsideRangeAndUsesExplicitBounds()
    {
        var stats = DynamoDbAggregateStatsBuilder.Build(
            SourceId,
            SampleOptions,
            ThreeEntriesAndTwoCategories,
            from: new DateOnly(2026, 1, 2),
            to: new DateOnly(2026, 1, 4),
            NoFallbackLookups);

        Assert.Equal(1, stats.TotalEntries);
        Assert.Equal(new DateOnly(2026, 1, 2), stats.Range.From);
        Assert.Equal(new DateOnly(2026, 1, 4), stats.Range.To);

        var category = Assert.Single(stats.Dimensions, d => d.Name == "category");
        var bucket = Assert.Single(category.Buckets);
        Assert.Equal("Category B", bucket.Value);
    }

    [Fact]
    public void Build_WithNoExplicitBounds_ResolvesRangeToMinAndMaxOfFilteredEntries()
    {
        var stats = DynamoDbAggregateStatsBuilder.Build(
            SourceId, SampleOptions, ThreeEntriesAndTwoCategories, from: null, to: null, NoFallbackLookups);

        Assert.Equal(new DateOnly(2026, 1, 1), stats.Range.From);
        Assert.Equal(new DateOnly(2026, 1, 5), stats.Range.To);
    }

    [Fact]
    public void Build_WithNoEntries_ResolvesRangeToTodayAndEmptyDimensionBuckets()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stats = DynamoDbAggregateStatsBuilder.Build(
            SourceId, SampleOptions, [], from: null, to: null, NoFallbackLookups);

        Assert.Equal(0, stats.TotalEntries);
        Assert.Equal(today, stats.Range.From);
        Assert.Equal(today, stats.Range.To);
        Assert.All(stats.Dimensions, d => Assert.Empty(d.Buckets));
    }

    [Fact]
    public void Build_NonColocatedLookup_UsesProvidedNameMapInsteadOfScanningPartitionItems()
    {
        var options = SampleOptions with
        {
            Dimensions =
            [
                new DimensionConfig("category", new LookupSource(
                    IdAttributeName: "categoryId",
                    LookupTableName: "SomeOtherTable",
                    LookupPartitionKeyValueTemplate: "USER#{sourceId}",
                    LookupSortKeyValueTemplate: "CAT#{categoryId}",
                    LookupNameAttribute: "name")),
            ],
        };
        var entriesOnly = new List<Dictionary<string, AttributeValue>> { EntryItem("cat-a", "2026-01-01T10:00:00Z") };
        var fallbackNames = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["category"] = new Dictionary<string, string> { ["cat-a"] = "Category A (fetched separately)" },
        };

        var stats = DynamoDbAggregateStatsBuilder.Build(
            SourceId, options, entriesOnly, from: null, to: null, fallbackNames);

        var category = Assert.Single(stats.Dimensions);
        var bucket = Assert.Single(category.Buckets);
        Assert.Equal("Category A (fetched separately)", bucket.Value);
    }

    [Fact]
    public void Build_LookupIdWithNoMatchingName_FallsBackToRawId()
    {
        var entriesOnly = new List<Dictionary<string, AttributeValue>>
        {
            EntryItem("unknown-category", "2026-01-01T10:00:00Z"),
        };

        var stats = DynamoDbAggregateStatsBuilder.Build(
            SourceId, SampleOptions, entriesOnly, from: null, to: null, NoFallbackLookups);

        var category = Assert.Single(stats.Dimensions, d => d.Name == "category");
        var bucket = Assert.Single(category.Buckets);
        Assert.Equal("unknown-category", bucket.Value);
    }

    [Fact]
    public void ExtractEntryItems_WithoutSortKeyConfigured_TreatsAllItemsAsEntries()
    {
        var options = SampleOptions with { SortKeyAttribute = null, EntrySortKeyPrefix = null };

        var extracted = DynamoDbAggregateStatsBuilder.ExtractEntryItems(options, ThreeEntriesAndTwoCategories);

        Assert.Equal(ThreeEntriesAndTwoCategories.Count, extracted.Count);
    }

    [Theory]
    [InlineData("CAT#{categoryId}", "CAT#")]
    [InlineData("USER#{sourceId}#CAT#{categoryId}", "USER#")]
    [InlineData("NoPlaceholder", "NoPlaceholder")]
    public void DerivePrefix_ReturnsEverythingBeforeTheFirstPlaceholder(string template, string expectedPrefix)
    {
        Assert.Equal(expectedPrefix, DynamoDbAggregateStatsBuilder.DerivePrefix(template));
    }

    [Fact]
    public void IsColocated_TrueWhenSameTableAndSamePartitionTemplate()
    {
        var lookup = new LookupSource("categoryId", LookupTableName: null, "USER#{sourceId}", "CAT#{categoryId}", "name");
        Assert.True(DynamoDbAggregateStatsBuilder.IsColocated(SampleOptions, lookup));
    }

    [Fact]
    public void IsColocated_FalseWhenLookupTableDiffers()
    {
        var lookup = new LookupSource("categoryId", LookupTableName: "OtherTable", "USER#{sourceId}", "CAT#{categoryId}", "name");
        Assert.False(DynamoDbAggregateStatsBuilder.IsColocated(SampleOptions, lookup));
    }

    [Fact]
    public void IsColocated_FalseWhenPartitionKeyTemplateDiffers()
    {
        var lookup = new LookupSource("categoryId", LookupTableName: null, "TENANT#{sourceId}", "CAT#{categoryId}", "name");
        Assert.False(DynamoDbAggregateStatsBuilder.IsColocated(SampleOptions, lookup));
    }
}
