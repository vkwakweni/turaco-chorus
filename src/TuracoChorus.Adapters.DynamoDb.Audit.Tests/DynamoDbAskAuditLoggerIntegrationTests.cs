using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.DynamoDb.Audit.Tests;

/// <summary>
/// Real write against a real, installer-owned DynamoDB table — no fake client. `IAuditLogger` is
/// write-only (see `DynamoDbAuditItemMapper`'s own doc comment), so this reads the item back via
/// a direct `GetItemAsync` call rather than through the adapter itself. Excluded from the default
/// test run (this project's own `VSTestTestCaseFilter` keeps a plain `dotnet test` green without
/// credentials); run with `dotnet test --filter "Category=Integration"`.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DynamoDbAskAuditLoggerIntegrationTests
{
    private const string TestUserId = "integration-test-audit-user";

    [Fact]
    public async Task RecordAuditEntryAsync_AgainstRealTable_WritesReadableItem()
    {
        var region = RequireEnv("TURACO_TEST_AWS_REGION");
        var tableName = RequireEnv("TURACO_TEST_DYNAMODB_AUDIT_TABLE_NAME");
        var options = new DynamoDbAskAuditLoggerOptions(TableName: tableName);

        using var client = new AmazonDynamoDBClient(
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) });
        var auditLogger = new DynamoDbAskAuditLogger(options, client);

        var timestamp = DateTimeOffset.UtcNow;
        var entry = new AuditEntry(
            UserId: TestUserId,
            QueryText: "integration test query",
            AggregatedDataSent: null,
            ConsentGranted: false,
            Outcome: AuditOutcome.Denied,
            Timestamp: timestamp);

        await auditLogger.RecordAuditEntryAsync(entry);

        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [DynamoDbAuditItemMapper.UserIdAttribute] = new AttributeValue { S = TestUserId },
                [DynamoDbAuditItemMapper.TimestampAttribute] = new AttributeValue { S = timestamp.ToString("O") },
            },
        });

        Assert.True(response.IsItemSet);
        Assert.Equal("integration test query", response.Item[DynamoDbAuditItemMapper.QueryTextAttribute].S);
        Assert.False(response.Item[DynamoDbAuditItemMapper.ConsentGrantedAttribute].BOOL);
        Assert.Equal("Denied", response.Item[DynamoDbAuditItemMapper.OutcomeAttribute].S);
        Assert.False(response.Item.ContainsKey(DynamoDbAuditItemMapper.AggregatedDataSentAttribute));
    }

    [Fact]
    public async Task RecordAuditEntryAsync_AgainstRealTable_RateLimitedEntryHasOutcomeAndNoConsentAttribute()
    {
        var region = RequireEnv("TURACO_TEST_AWS_REGION");
        var tableName = RequireEnv("TURACO_TEST_DYNAMODB_AUDIT_TABLE_NAME");
        var options = new DynamoDbAskAuditLoggerOptions(TableName: tableName);

        using var client = new AmazonDynamoDBClient(
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) });
        var auditLogger = new DynamoDbAskAuditLogger(options, client);

        var timestamp = DateTimeOffset.UtcNow;
        await auditLogger.RecordAuditEntryAsync(new AuditEntry(
            UserId: TestUserId,
            QueryText: "integration test rate limited query",
            AggregatedDataSent: null,
            ConsentGranted: null,
            Outcome: AuditOutcome.RateLimited,
            Timestamp: timestamp));

        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [DynamoDbAuditItemMapper.UserIdAttribute] = new AttributeValue { S = TestUserId },
                [DynamoDbAuditItemMapper.TimestampAttribute] = new AttributeValue { S = timestamp.ToString("O") },
            },
        });

        Assert.True(response.IsItemSet);
        Assert.Equal("RateLimited", response.Item[DynamoDbAuditItemMapper.OutcomeAttribute].S);
        Assert.False(response.Item.ContainsKey(DynamoDbAuditItemMapper.ConsentGrantedAttribute));
    }

    private static string RequireEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Integration test requires environment variable \"{name}\" to be set.");
}
