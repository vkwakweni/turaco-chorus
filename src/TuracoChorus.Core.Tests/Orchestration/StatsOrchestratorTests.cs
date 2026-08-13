using TuracoChorus.Core.Models;
using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Fakes;
using Xunit;

namespace TuracoChorus.Core.Tests.Orchestration;

public sealed class StatsOrchestratorTests
{
    [Fact]
    public async Task GetStatsAsync_ReturnsStatsFromLogDataSource()
    {
        var logDataSource = new FakeLogDataSource();
        var expectedStats = new AggregateStats(
            SourceId: "user-1",
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 5,
            Categories: [new CategoryCount("mood", 5)],
            EntriesByDate: [new DateCount(new DateOnly(2026, 1, 15), 5)]);
        logDataSource.Seed("user-1", expectedStats);
        var orchestrator = new StatsOrchestrator(logDataSource);

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        var result = await orchestrator.GetStatsAsync("user-1", from, to);

        // Check that the data requested stayed consistent
        Assert.Equal(("user-1", (DateOnly?)from, (DateOnly?)to), logDataSource.LastRequest);

        // Check that the data is unchanged through the orchestrator
        Assert.Same(expectedStats, result);
    }

    [Fact]
    public async Task GetStatsAsync_UsesTheGivenUserId_NotSomeoneElses()
    {
        var logDataSource = new FakeLogDataSource();
        var statsForUser1 = new AggregateStats(
            SourceId: "user-1",
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 5,
            Categories: [new CategoryCount("mood", 5)],
            EntriesByDate: [new DateCount(new DateOnly(2026, 1, 15), 5)]);
        var statsForUser2 = new AggregateStats(
            SourceId: "user-2",
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 1,
            Categories: [new CategoryCount("sleep", 1)],
            EntriesByDate: [new DateCount(new DateOnly(2026, 1, 20), 1)]);
        logDataSource.Seed("user-1", statsForUser1);
        logDataSource.Seed("user-2", statsForUser2);
        var orchestrator = new StatsOrchestrator(logDataSource);

        var result = await orchestrator.GetStatsAsync("user-2", from: null, to: null);

        // Confirm the orchestrator requested the user actually asked for
        Assert.Equal("user-2", logDataSource.LastRequest?.SourceId);

        // Confirm that what went through the orchestrator is still associated with the correct user
        Assert.Same(statsForUser2, result);
    }
}
