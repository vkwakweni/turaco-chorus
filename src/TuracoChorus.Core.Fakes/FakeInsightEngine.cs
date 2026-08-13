using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Fakes;

public sealed class FakeInsightEngine : IInsightEngine
{
    public RequestedRange RangeToReturn { get; set; } = new(From: null, To: null);
    public Answer AnswerToReturn { get; set; } = new(
        Text: "fake answer",
        DataUsed: new DataUsed(StatsQueried: [], Range: new DateRange(DateOnly.MinValue, DateOnly.MinValue)));

    public AggregateStats? LastStatsReceived { get; private set; }
    public int ExtractRangeCallCount { get; private set; }
    public int AskCallCount { get; private set; }

    public Task<RequestedRange> ExtractRangeAsync(string question)
    {
        ExtractRangeCallCount++;
        return Task.FromResult(RangeToReturn);
    }

    public Task<Answer> AskAsync(AggregateStats stats, string question)
    {
        AskCallCount++;
        LastStatsReceived = stats;
        return Task.FromResult(AnswerToReturn);
    }
}
