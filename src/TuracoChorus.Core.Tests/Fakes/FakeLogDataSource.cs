using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Tests.Fakes;

public sealed class FakeLogDataSource : ILogDataSource
{
    private readonly Dictionary<string, AggregateStats> _statsBySourceId = new();

    public (string SourceId, DateOnly? From, DateOnly? To)? LastRequest { get; private set; }

    public void Seed(string sourceId, AggregateStats stats)
        => _statsBySourceId[sourceId] = stats;

    public Task<AggregateStats> GetStatsAsync(string sourceId, DateOnly? from, DateOnly? to)
    {
        LastRequest = (sourceId, from, to);
        return _statsBySourceId.TryGetValue(sourceId, out var stats)
            ? Task.FromResult(stats)
            : throw new InvalidOperationException($"No stats seeded for source '{sourceId}'.");
    }
}
