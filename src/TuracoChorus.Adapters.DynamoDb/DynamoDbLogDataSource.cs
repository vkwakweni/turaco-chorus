using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.DynamoDb;

/// <summary>
/// Reads a user's log entries out of whatever DynamoDB table it's configured to point at, and
/// maps them into AggregateStats purely through configuration — no installer-specific code.
/// See artifacts/dynamodb-adapter.md for the full design.
/// </summary>
public sealed class DynamoDbLogDataSource(
    DynamoDbLogDataSourceOptions options, IAmazonDynamoDB client) : ILogDataSource
{
    public async Task<AggregateStats> GetStatsAsync(string sourceId, DateOnly? from, DateOnly? to)
    {
        var partitionKeyValue = Substitute(options.PartitionKeyValueTemplate, sourceId);
        var partitionItems = await QueryPartitionAsync(partitionKeyValue);

        var nonColocatedLookupNames = await ResolveNonColocatedLookupsAsync(sourceId, partitionItems);

        return DynamoDbAggregateStatsBuilder.Build(
            sourceId, options, partitionItems, from, to, nonColocatedLookupNames);
    }

    /// <summary>
    /// One Query per request. Fetches the whole partition (no sort-key condition) only when at
    /// least one configured Lookup dimension is colocated with entries — that's what lets a single
    /// read resolve both entries and lookup definitions together. Otherwise, narrows to entries
    /// only via begins_with(SortKeyAttribute, EntrySortKeyPrefix), when both are configured.
    /// </summary>
    private async Task<List<Dictionary<string, AttributeValue>>> QueryPartitionAsync(string partitionKeyValue)
    {
        var needsFullPartition = options.Dimensions.Any(dimension =>
            dimension.Source is LookupSource lookup && DynamoDbAggregateStatsBuilder.IsColocated(options, lookup));

        var canScopeToEntries = !needsFullPartition
            && options.SortKeyAttribute is not null
            && options.EntrySortKeyPrefix is not null;

        var request = new QueryRequest
        {
            TableName = options.TableName,
            KeyConditionExpression = canScopeToEntries
                ? "#pk = :pk AND begins_with(#sk, :skPrefix)"
                : "#pk = :pk",
            ExpressionAttributeNames = canScopeToEntries
                ? new Dictionary<string, string> { ["#pk"] = options.PartitionKeyAttribute, ["#sk"] = options.SortKeyAttribute! }
                : new Dictionary<string, string> { ["#pk"] = options.PartitionKeyAttribute },
            ExpressionAttributeValues = canScopeToEntries
                ? new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = partitionKeyValue },
                    [":skPrefix"] = new AttributeValue { S = options.EntrySortKeyPrefix! },
                }
                : new Dictionary<string, AttributeValue> { [":pk"] = new AttributeValue { S = partitionKeyValue } },
        };

        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;
        do
        {
            request.ExclusiveStartKey = lastEvaluatedKey;
            var response = await client.QueryAsync(request);
            items.AddRange(response.Items);
            lastEvaluatedKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (lastEvaluatedKey is not null);

        return items;
    }

    /// <summary>
    /// Per-id fallback for Lookup dimensions whose definitions aren't colocated with entries.
    /// Documented, accepted N+1 limitation (see dynamodb-adapter.md) — one GetItem per distinct
    /// id per dimension, no caching. Runs against the unfiltered entry batch, so it may resolve a
    /// few more ids than end up in the date-filtered result; simpler than filtering twice.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> ResolveNonColocatedLookupsAsync(
        string sourceId, IReadOnlyList<Dictionary<string, AttributeValue>> partitionItems)
    {
        var entryItems = DynamoDbAggregateStatsBuilder.ExtractEntryItems(options, partitionItems);
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        foreach (var dimension in options.Dimensions)
        {
            if (dimension.Source is not LookupSource lookup
                || DynamoDbAggregateStatsBuilder.IsColocated(options, lookup))
            {
                continue;
            }

            var distinctIds = entryItems
                .Select(entry => entry.TryGetValue(lookup.IdAttributeName, out var idAttr) ? idAttr.S : null)
                .Where(id => id is not null)
                .Select(id => id!)
                .Distinct();

            var namesById = new Dictionary<string, string>();
            foreach (var id in distinctIds)
            {
                var name = await FetchLookupNameAsync(sourceId, lookup, id);
                if (name is not null)
                {
                    namesById[id] = name;
                }
            }

            result[dimension.Name] = namesById;
        }

        return result;
    }

    private async Task<string?> FetchLookupNameAsync(string sourceId, LookupSource lookup, string id)
    {
        if (options.SortKeyAttribute is null)
        {
            throw new InvalidOperationException(
                "A Lookup dimension requires SortKeyAttribute to be configured.");
        }

        var partitionKeyValue = Substitute(lookup.LookupPartitionKeyValueTemplate, sourceId);
        var sortKeyValue = lookup.LookupSortKeyValueTemplate.Replace($"{{{lookup.IdAttributeName}}}", id);

        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = lookup.LookupTableName ?? options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [options.PartitionKeyAttribute] = new AttributeValue { S = partitionKeyValue },
                [options.SortKeyAttribute] = new AttributeValue { S = sortKeyValue },
            },
        });

        return response.Item.TryGetValue(lookup.LookupNameAttribute, out var nameAttr) ? nameAttr.S : null;
    }

    private static string Substitute(string template, string sourceId)
        => template.Replace("{sourceId}", sourceId);
}
