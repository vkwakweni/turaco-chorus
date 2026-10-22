using TuracoChorus.Adapters.DynamoDb.Audit;
using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.DynamoDb.Audit.Tests;

public class DynamoDbAuditItemMapperTests
{
    private static readonly DateTimeOffset SomeTimestamp = new(2026, 8, 24, 14, 30, 0, TimeSpan.Zero);

    private static AggregateStats SomeStats() => new(
        SourceId: "user-1",
        Range: new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 24)),
        TotalEntries: 5,
        Dimensions:
        [
            new Dimension("category", [new DimensionBucket("books", 3), new DimensionBucket("movies", 2)]),
        ]);

    [Fact]
    public void ToPutItem_DeniedEntry_OmitsAggregatedDataSent()
    {
        var entry = new AuditEntry(
            UserId: "user-1",
            QueryText: "How many entries did I log last month?",
            AggregatedDataSent: null,
            ConsentGranted: false,
            Outcome: AuditOutcome.Denied,
            Timestamp: SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Equal("user-1", item["userId"].S);
        Assert.Equal("2026-08-24T14:30:00.0000000+00:00", item["timestamp"].S);
        Assert.Equal("How many entries did I log last month?", item["queryText"].S);
        Assert.False(item["consentGranted"].BOOL);
        Assert.Equal("Denied", item["outcome"].S);
        Assert.False(item.ContainsKey("aggregatedDataSent"));
    }

    [Fact]
    public void ToPutItem_AllowedEntry_IncludesAggregatedDataSent()
    {
        var entry = new AuditEntry(
            UserId: "user-1",
            QueryText: "How many entries did I log last month?",
            AggregatedDataSent: SomeStats(),
            ConsentGranted: true,
            Outcome: AuditOutcome.Answered,
            Timestamp: SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.True(item["consentGranted"].BOOL);
        Assert.Equal("Answered", item["outcome"].S);

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
            Outcome: AuditOutcome.Answered,
            Timestamp: SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Empty(item["aggregatedDataSent"].M["dimensions"].L);
    }

    [Theory]
    [InlineData(AuditOutcome.Answered, "Answered")]
    [InlineData(AuditOutcome.Denied, "Denied")]
    [InlineData(AuditOutcome.CouldNotAnswer, "CouldNotAnswer")]
    [InlineData(AuditOutcome.Failed, "Failed")]
    [InlineData(AuditOutcome.RateLimited, "RateLimited")]
    public void ToPutItem_WritesTheOutcomeAsItsName_ForEveryOutcome(AuditOutcome outcome, string expected)
    {
        var entry = new AuditEntry("user-1", "q", AggregatedDataSent: null, ConsentGranted: true, outcome, SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Equal(expected, item["outcome"].S);
    }

    [Fact]
    public void ToPutItem_EveryOutcomeValueIsCoveredByTheTheoryAbove()
    {
        // Adding an AuditOutcome without a mapper test should fail here, not slip through.
        Assert.Equal(5, Enum.GetValues<AuditOutcome>().Length);
    }

    [Fact]
    public void ToPutItem_NullConsentGranted_OmitsTheConsentGrantedAttribute()
    {
        var entry = new AuditEntry(
            "user-1", "q", AggregatedDataSent: null, ConsentGranted: null, AuditOutcome.RateLimited, SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.False(item.ContainsKey("consentGranted"));
        Assert.Equal("RateLimited", item["outcome"].S);
    }

    [Fact]
    public void ToPutItem_FailedEntryWithStats_IncludesAggregatedDataSent()
    {
        var entry = new AuditEntry(
            "user-1", "q", AggregatedDataSent: SomeStats(), ConsentGranted: true, AuditOutcome.Failed, SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Equal("Failed", item["outcome"].S);
        Assert.True(item["consentGranted"].BOOL);
        Assert.Equal("5", item["aggregatedDataSent"].M["totalEntries"].N);
    }

    [Fact]
    public void ToPutItem_FailedEntryWithoutStats_OmitsAggregatedDataSent()
    {
        var entry = new AuditEntry(
            "user-1", "q", AggregatedDataSent: null, ConsentGranted: true, AuditOutcome.Failed, SomeTimestamp);

        var item = DynamoDbAuditItemMapper.ToPutItem(entry);

        Assert.Equal("Failed", item["outcome"].S);
        Assert.False(item.ContainsKey("aggregatedDataSent"));
    }
}
