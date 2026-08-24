using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.DynamoDb.Audit;

/// <summary>
/// Pure mapping from AuditEntry to TuracoChorusAskAudit's item shape — no AWS calls, one
/// direction only (write-only port, no read side). See artifacts/tech-stack.md's "Storage
/// schemas" section for the item shape.
/// </summary>
public static class DynamoDbAuditItemMapper
{
    public const string UserIdAttribute = "userId";
    public const string TimestampAttribute = "timestamp";
    public const string QueryTextAttribute = "queryText";
    public const string ConsentGrantedAttribute = "consentGranted";
    public const string AggregatedDataSentAttribute = "aggregatedDataSent";

    /// <summary>aggregatedDataSent is omitted entirely when AggregatedDataSent is null (the consent-denied path).</summary>
    public static Dictionary<string, AttributeValue> ToPutItem(AuditEntry entry)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [UserIdAttribute] = new AttributeValue { S = entry.UserId },
            [TimestampAttribute] = new AttributeValue { S = entry.Timestamp.ToString("O") },
            [QueryTextAttribute] = new AttributeValue { S = entry.QueryText },
            [ConsentGrantedAttribute] = new AttributeValue { BOOL = entry.ConsentGranted },
        };

        if (entry.AggregatedDataSent is { } stats)
        {
            item[AggregatedDataSentAttribute] = ToAttributeValue(stats);
        }

        return item;
    }

    private static AttributeValue ToAttributeValue(AggregateStats stats) => new()
    {
        M = new Dictionary<string, AttributeValue>
        {
            ["sourceId"] = new AttributeValue { S = stats.SourceId },
            ["range"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["from"] = new AttributeValue { S = stats.Range.From.ToString("yyyy-MM-dd") },
                    ["to"] = new AttributeValue { S = stats.Range.To.ToString("yyyy-MM-dd") },
                },
            },
            ["totalEntries"] = new AttributeValue { N = stats.TotalEntries.ToString() },
            ["dimensions"] = new AttributeValue
            {
                L = stats.Dimensions.Select(ToAttributeValue).ToList(),
            },
        },
    };

    private static AttributeValue ToAttributeValue(Dimension dimension) => new()
    {
        M = new Dictionary<string, AttributeValue>
        {
            ["name"] = new AttributeValue { S = dimension.Name },
            ["buckets"] = new AttributeValue
            {
                L = dimension.Buckets.Select(ToAttributeValue).ToList(),
            },
        },
    };

    private static AttributeValue ToAttributeValue(DimensionBucket bucket) => new()
    {
        M = new Dictionary<string, AttributeValue>
        {
            ["value"] = new AttributeValue { S = bucket.Value },
            ["count"] = new AttributeValue { N = bucket.Count.ToString() },
        },
    };
}
