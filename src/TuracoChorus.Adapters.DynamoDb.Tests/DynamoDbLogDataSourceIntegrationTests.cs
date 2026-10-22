using Amazon;
using Amazon.DynamoDBv2;

namespace TuracoChorus.Adapters.DynamoDb.Tests;

/// <summary>
/// Reads from a real, installer-owned DynamoDB table over the network — no hand-built items, no
/// fake client. Excluded from the default test run (this project's own `VSTestTestCaseFilter`
/// keeps a plain `dotnet test` green without credentials); run with
/// `dotnet test --filter "Category=Integration"`.
///
/// Configured with an empty `Dimensions` list (valid per `dynamodb-adapter.md`: total-only
/// stats) — dimension-resolution logic (colocated lookups, the N+1 fallback) is already
/// thoroughly covered against hand-built items in `DynamoDbAggregateStatsBuilderTests.cs`; this
/// test's only job is proving the real AWS SDK round-trip (auth, network, table/partition lookup)
/// actually works, not re-verifying bucketing logic against real data.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DynamoDbLogDataSourceIntegrationTests
{
    [Fact]
    public async Task GetStatsAsync_AgainstRealTable_ReturnsStatsForSourceId()
    {
        var region = RequireEnv("TURACO_TEST_AWS_REGION");
        var sourceId = RequireEnv("TURACO_TEST_DYNAMODB_SOURCE_ID");

        var options = new DynamoDbLogDataSourceOptions(
            TableName: RequireEnv("TURACO_TEST_DYNAMODB_TABLE_NAME"),
            PartitionKeyAttribute: RequireEnv("TURACO_TEST_DYNAMODB_PARTITION_KEY_ATTRIBUTE"),
            PartitionKeyValueTemplate: RequireEnv("TURACO_TEST_DYNAMODB_PARTITION_KEY_VALUE_TEMPLATE"),
            DateAttribute: RequireEnv("TURACO_TEST_DYNAMODB_DATE_ATTRIBUTE"),
            Dimensions: [],
            SortKeyAttribute: Environment.GetEnvironmentVariable("TURACO_TEST_DYNAMODB_SORT_KEY_ATTRIBUTE"),
            EntrySortKeyPrefix: Environment.GetEnvironmentVariable("TURACO_TEST_DYNAMODB_ENTRY_SORT_KEY_PREFIX"));

        using var client = new AmazonDynamoDBClient(
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) });
        var logDataSource = new DynamoDbLogDataSource(options, client);

        var stats = await logDataSource.GetStatsAsync(sourceId, from: null, to: null);

        Assert.Equal(sourceId, stats.SourceId);
        Assert.True(stats.TotalEntries >= 0);
    }

    private static string RequireEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Integration test requires environment variable \"{name}\" to be set.");
}
