using TuracoChorus.Adapters.DynamoDb.Audit;
using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.DynamoDb.Audit.Tests;

public class DynamoDbAuditItemMapperTests
{
    [Fact]
    public void ToPutItem_DeniedEntry_OmitsAggregatedDataSent()
    {
        var entry = new AuditEntry(
            UserId: "user-1",
            QueryText: "How many entries did I log last month?",
            AggregatedDataSent: null,
            ConsentGranted: false,
            Timestamp: new DateTimeOffset(2026, 8, 24, 14, 30, 0, TimeSpan.Zero));

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Equal("user-1", item["userId"].S);
        Assert.Equal("2026-08-24T14:30:00.0000000+00:00", item["timestamp"].S);
        Assert.Equal("How many entries did I log last month?", item["queryText"].S);
        Assert.False(item["consentGranted"].BOOL);
        Assert.False(item.ContainsKey("aggregatedDataSent"));
    }

    [Fact]
    public void ToPutItem_AllowedEntry_IncludesAggregatedDataSent()
    {
        var stats = new AggregateStats(
            SourceId: "user-1",
            Range: new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 24)),
            TotalEntries: 5,
            Dimensions:
            [
                new Dimension("category", [new DimensionBucket("books", 3), new DimensionBucket("movies", 2)]),
            ]);

        var entry = new AuditEntry(
            UserId: "user-1",
            QueryText: "How many entries did I log last month?",
            AggregatedDataSent: stats,
            ConsentGranted: true,
            Timestamp: new DateTimeOffset(2026, 8, 24, 14, 30, 0, TimeSpan.Zero));

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.True(item["consentGranted"].BOOL);

        var statsMap = item["aggregatedDataSent"].M;
        Assert.Equal("user-1", statsMap["sourceId"].S);
        Assert.Equal("5", statsMap["totalEntries"].N);

        var range = statsMap["range"].M;
        Assert.Equal("2026-08-01", range["from"].S);
        Assert.Equal("2026-08-24", range["to"].S);

        var dimensions = statsMap["dimensions"].L;
        Assert.Single(dimensions);
        var categoryDimension = dimensions[0].M;
        Assert.Equal("category", categoryDimension["name"].S);

        var buckets = categoryDimension["buckets"].L;
        Assert.Equal(2, buckets.Count);
        Assert.Equal("books", buckets[0].M["value"].S);
        Assert.Equal("3", buckets[0].M["count"].N);
        Assert.Equal("movies", buckets[1].M["value"].S);
        Assert.Equal("2", buckets[1].M["count"].N);
    }

    [Fact]
    public void ToPutItem_AllowedEntry_EmptyDimensions_MapsToEmptyList()
    {
        var stats = new AggregateStats(
            SourceId: "user-1",
            Range: new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 24)),
            TotalEntries: 0,
            Dimensions: []);

        var entry = new AuditEntry(
            UserId: "user-1",
            QueryText: "Anything logged?",
            AggregatedDataSent: stats,
            ConsentGranted: true,
            Timestamp: new DateTimeOffset(2026, 8, 24, 14, 30, 0, TimeSpan.Zero));

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Empty(item["aggregatedDataSent"].M["dimensions"].L);
    }
}
