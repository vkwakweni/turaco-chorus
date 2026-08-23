namespace TuracoChorus.Adapters.DynamoDb;

public sealed record DynamoDbLogDataSourceOptions(
    string TableName,
    string Region,
    string PartitionKeyAttribute,
    string PartitionKeyValueTemplate,
    string? SortKeyAttribute,
    string? EntrySortKeyPrefix,
    string DateAttribute,
    IReadOnlyList<DimensionConfig> Dimensions);

public sealed record DimensionConfig(string Name, DimensionSource Source);

/// <summary>Where a dimension's bucket values come from — a flat attribute, or an id needing a lookup.</summary>
public abstract record DimensionSource;

public sealed record DirectAttributeSource(string AttributeName) : DimensionSource;

/// <summary>
/// LookupTableName defaults to the entries' own TableName when null — a dimension whose
/// definition items are colocated with entries never sets it explicitly.
/// </summary>
public sealed record LookupSource(
    string IdAttributeName,
    string? LookupTableName,
    string LookupPartitionKeyValueTemplate,
    string LookupSortKeyValueTemplate,
    string LookupNameAttribute) : DimensionSource;
