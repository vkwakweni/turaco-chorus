using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Orchestration;

public sealed class StatsOrchestrator(ILogDataSource logDataSource)
{
    public Task<AggregateStats> GetStatsAsync(string userId, DateOnly? from, DateOnly? to)
        => logDataSource.GetStatsAsync(userId, from, to);
}
