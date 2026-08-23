namespace TuracoChorus.Contracts;

public sealed record DateRangeResponse(DateOnly From, DateOnly To);

public sealed record DimensionBucketResponse(string Value, int Count);

public sealed record DimensionResponse(string Name, IReadOnlyList<DimensionBucketResponse> Buckets);

public sealed record AggregateStatsResponse(
    DateRangeResponse Range,
    int TotalEntries,
    IReadOnlyList<DimensionResponse> Dimensions);
