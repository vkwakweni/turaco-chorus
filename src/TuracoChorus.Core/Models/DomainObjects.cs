namespace TuracoChorus.Core.Models;

public sealed record RequestedRange(DateOnly? From, DateOnly? To);

public sealed record DateRange(DateOnly From, DateOnly To);

public sealed record CategoryCount(string Name, int Count);

public sealed record DateCount(DateOnly Date, int Count);

public sealed record AggregateStats(
    string SourceId,
    DateRange Range,
    int TotalEntries,
    IReadOnlyList<CategoryCount> Categories,
    IReadOnlyList<DateCount> EntriesByDate);

public sealed record ConsentRecord(string UserId, bool Granted, DateOnly? GrantedAt);

public sealed record DataUsed(IReadOnlyList<string> StatsQueried, DateRange Range);

public sealed record Answer(string Text, DataUsed DataUsed, bool ConsentVerified);

public sealed record AuditEntry(
    string UserId,
    string QueryText,
    AggregateStats? AggregatedDataSent,
    bool ConsentGranted,
    DateTimeOffset Timestamp);
