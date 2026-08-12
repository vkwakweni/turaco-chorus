using TuracoChorus.Core.Models;

namespace TuracoChorus.Core.Ports;

public interface ILogDataSource
{
    Task<AggregateStats> GetStatsAsync(string sourceId, DateOnly? from, DateOnly? to);
}
