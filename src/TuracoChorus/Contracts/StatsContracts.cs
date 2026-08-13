namespace TuracoChorus.Contracts;

public sealed record DateRangeResponse(DateOnly From, DateOnly To);

public sealed record CategoryCountResponse(string Name, int Count);

public sealed record DateCountResponse(DateOnly Date, int Count);

public sealed record AggregateStatsResponse(
    DateRangeResponse Range,
    int TotalEntries,
    IReadOnlyList<CategoryCountResponse> Categories,
    IReadOnlyList<DateCountResponse> EntriesByDate);
