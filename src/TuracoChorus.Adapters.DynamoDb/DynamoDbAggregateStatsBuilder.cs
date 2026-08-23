using System.Globalization;
using System.Runtime.CompilerServices;
using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;

[assembly: InternalsVisibleTo("TuracoChorus.Adapters.DynamoDb.Tests")]

namespace TuracoChorus.Adapters.DynamoDb;

/// <summary>
/// Turns a batch of raw DynamoDB items already fetched from one partition into an AggregateStats.
/// Deliberately has no AWS SDK client dependency — everything here is pure, given the items,
/// so it's testable against hand-built items with no real table needed.
/// </summary>
internal static class DynamoDbAggregateStatsBuilder
{
    public static AggregateStats Build(
        string sourceId,
        DynamoDbLogDataSourceOptions options,
        IReadOnlyList<Dictionary<string, AttributeValue>> partitionItems,
        DateOnly? from,
        DateOnly? to,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> nonColocatedLookupNamesByDimension)
    {
        var entryItems = ExtractEntryItems(options, partitionItems);
        var filteredEntries = FilterByDateRange(options.DateAttribute, entryItems, from, to);
        var range = ResolveRange(options.DateAttribute, filteredEntries, from, to);

        var dimensions = options.Dimensions
            .Select(dimension => ResolveDimension(
                options, dimension, partitionItems, filteredEntries, nonColocatedLookupNamesByDimension))
            .ToList();

        return new AggregateStats(sourceId, range, filteredEntries.Count, dimensions);
    }

    public static List<Dictionary<string, AttributeValue>> ExtractEntryItems(
        DynamoDbLogDataSourceOptions options,
        IReadOnlyList<Dictionary<string, AttributeValue>> partitionItems)
    {
        if (options.SortKeyAttribute is null || options.EntrySortKeyPrefix is null)
        {
            return partitionItems.ToList();
        }

        return partitionItems
            .Where(item => SortKeyStartsWith(item, options.SortKeyAttribute, options.EntrySortKeyPrefix))
            .ToList();
    }

    /// <summary>
    /// A Lookup dimension is colocated when its definition items live in the same table and the
    /// same partition as the entries — meaning the earlier partition-wide query already returned
    /// them, and no extra fetch is needed to resolve display names.
    /// </summary>
    public static bool IsColocated(DynamoDbLogDataSourceOptions options, LookupSource lookup)
        => (lookup.LookupTableName ?? options.TableName) == options.TableName
           && lookup.LookupPartitionKeyValueTemplate == options.PartitionKeyValueTemplate;

    /// <summary>Everything before a template's first placeholder — e.g. "TYPE#" from "TYPE#{typeId}".</summary>
    public static string DerivePrefix(string template)
    {
        var braceIndex = template.IndexOf('{');
        return braceIndex >= 0 ? template[..braceIndex] : template;
    }

    private static bool SortKeyStartsWith(Dictionary<string, AttributeValue> item, string sortKeyAttribute, string prefix)
        => item.TryGetValue(sortKeyAttribute, out var sk)
           && sk.S is not null
           && sk.S.StartsWith(prefix, StringComparison.Ordinal);

    private static List<Dictionary<string, AttributeValue>> FilterByDateRange(
        string dateAttribute,
        IReadOnlyList<Dictionary<string, AttributeValue>> entries,
        DateOnly? from,
        DateOnly? to)
        => entries
            .Where(entry =>
            {
                var date = ParseDate(entry, dateAttribute);
                return (from is null || date >= from) && (to is null || date <= to);
            })
            .ToList();

    private static DateOnly ParseDate(Dictionary<string, AttributeValue> item, string dateAttribute)
    {
        var raw = item.TryGetValue(dateAttribute, out var value) ? value.S : null;
        if (raw is null)
        {
            throw new InvalidOperationException($"Item is missing string attribute \"{dateAttribute}\".");
        }

        return DateOnly.FromDateTime(DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static DateRange ResolveRange(
        string dateAttribute,
        IReadOnlyList<Dictionary<string, AttributeValue>> filteredEntries,
        DateOnly? from,
        DateOnly? to)
    {
        var dates = filteredEntries.Select(entry => ParseDate(entry, dateAttribute)).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var resolvedFrom = from ?? (dates.Count > 0 ? dates.Min() : today);
        var resolvedTo = to ?? (dates.Count > 0 ? dates.Max() : today);
        return new DateRange(resolvedFrom, resolvedTo);
    }

    private static Dimension ResolveDimension(
        DynamoDbLogDataSourceOptions options,
        DimensionConfig dimension,
        IReadOnlyList<Dictionary<string, AttributeValue>> partitionItems,
        IReadOnlyList<Dictionary<string, AttributeValue>> filteredEntries,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> nonColocatedLookupNamesByDimension)
        => dimension.Source switch
        {
            DirectAttributeSource direct => ResolveDirectAttributeDimension(dimension.Name, direct, filteredEntries),
            LookupSource lookup => ResolveLookupDimension(
                options, dimension.Name, lookup, partitionItems, filteredEntries, nonColocatedLookupNamesByDimension),
            var other => throw new NotSupportedException($"Unknown dimension source type: {other.GetType()}"),
        };

    private static Dimension ResolveDirectAttributeDimension(
        string name, DirectAttributeSource source, IReadOnlyList<Dictionary<string, AttributeValue>> entries)
    {
        var buckets = entries
            .Select(entry => entry.TryGetValue(source.AttributeName, out var v) ? v.S : null)
            .Where(value => value is not null)
            .GroupBy(value => value!)
            .Select(group => new DimensionBucket(group.Key, group.Count()))
            .ToList();

        return new Dimension(name, buckets);
    }

    private static Dimension ResolveLookupDimension(
        DynamoDbLogDataSourceOptions options,
        string name,
        LookupSource lookup,
        IReadOnlyList<Dictionary<string, AttributeValue>> partitionItems,
        IReadOnlyList<Dictionary<string, AttributeValue>> entries,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> nonColocatedLookupNamesByDimension)
    {
        var namesById = IsColocated(options, lookup)
            ? BuildColocatedLookupMap(options, lookup, partitionItems)
            : nonColocatedLookupNamesByDimension.TryGetValue(name, out var fetched)
                ? fetched
                : new Dictionary<string, string>();

        var buckets = entries
            .Select(entry => entry.TryGetValue(lookup.IdAttributeName, out var idAttr) ? idAttr.S : null)
            .Where(id => id is not null)
            .Select(id => namesById.TryGetValue(id!, out var displayName) ? displayName : id!)
            .GroupBy(value => value)
            .Select(group => new DimensionBucket(group.Key, group.Count()))
            .ToList();

        return new Dimension(name, buckets);
    }

    private static Dictionary<string, string> BuildColocatedLookupMap(
        DynamoDbLogDataSourceOptions options,
        LookupSource lookup,
        IReadOnlyList<Dictionary<string, AttributeValue>> partitionItems)
    {
        if (options.SortKeyAttribute is null)
        {
            throw new InvalidOperationException(
                "A colocated Lookup dimension requires SortKeyAttribute to distinguish lookup items from entries.");
        }

        var prefix = DerivePrefix(lookup.LookupSortKeyValueTemplate);
        var map = new Dictionary<string, string>();

        foreach (var item in partitionItems)
        {
            if (!item.TryGetValue(options.SortKeyAttribute, out var sk)
                || sk.S is null
                || !sk.S.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetValue(lookup.LookupNameAttribute, out var nameAttr) || nameAttr.S is null)
            {
                continue;
            }

            map[sk.S[prefix.Length..]] = nameAttr.S;
        }

        return map;
    }
}
