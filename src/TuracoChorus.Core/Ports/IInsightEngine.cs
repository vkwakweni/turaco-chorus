using TuracoChorus.Core.Models;

namespace TuracoChorus.Core.Ports;

public interface IInsightEngine
{
    Task<RequestedRange> ExtractRangeAsync(string question);
    Task<Answer> AskAsync(AggregateStats stats, string question);
}
