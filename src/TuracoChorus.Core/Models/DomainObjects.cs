namespace TuracoChorus.Core.Models;

public sealed record RequestedRange(DateOnly? From, DateOnly? To);

public sealed record DateRange(DateOnly From, DateOnly To);

/// <summary>One value within a Dimension, and how many entries fall under it.</summary>
public sealed record DimensionBucket(string Value, int Count);

/// <summary>
/// One installer-defined breakdown of a user's entries (e.g. "category" or "date").
/// Bucket values should be unique within Buckets; order is adapter-determined, not contractual.
/// </summary>
public sealed record Dimension(string Name, IReadOnlyList<DimensionBucket> Buckets);

/// <summary>
/// Dimensions are entirely installer-defined via the ILogDataSource adapter's configuration —
/// no dimension name (including "date") is built in. Dimension names should be unique within
/// Dimensions; order is adapter-determined, not contractual. An empty list is valid (total-only stats).
/// </summary>
public sealed record AggregateStats(
    string SourceId,
    DateRange Range,
    int TotalEntries,
    IReadOnlyList<Dimension> Dimensions);

public sealed record ConsentRecord(string UserId, bool Granted, DateOnly? GrantedAt);

public sealed record DataUsed(IReadOnlyList<string> StatsQueried, DateRange Range);

public sealed record Answer(string Text, DataUsed DataUsed);

public sealed record AuditEntry(
    string UserId,
    string QueryText,
    AggregateStats? AggregatedDataSent,
    bool ConsentGranted,
    DateTimeOffset Timestamp);
