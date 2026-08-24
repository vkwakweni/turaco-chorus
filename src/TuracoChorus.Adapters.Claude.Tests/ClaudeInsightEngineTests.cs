using System.Net;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.Claude.Tests;

/// <summary>
/// Confirms ClaudeInsightEngine's pieces are actually wired together correctly (HTTP response
/// parsing, the completion-status check, and delegation into ClaudeResponseParser) — not an
/// exhaustive case-by-case sweep. Every stop_reason value and its exact failure behavior is
/// already covered atomically in ClaudeResponseParserTests against EnsureNormalCompletion
/// directly, with no HTTP mocking needed there.
/// </summary>
public sealed class ClaudeInsightEngineTests
{
    private static readonly AggregateStats SampleStats = new(
        SourceId: "user-1",
        Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
        TotalEntries: 3,
        Dimensions: [new Dimension("category", [new DimensionBucket("Category A", 3)])]);

    private static ClaudeInsightEngine BuildEngine(string responseBody)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var httpClient = new HttpClient(handler);
        return new ClaudeInsightEngine(new ClaudeInsightEngineOptions(ApiKey: "test-key"), httpClient);
    }

    [Fact]
    public async Task AskAsync_WhenStopReasonIsEndTurn_ReturnsAnswer()
    {
        var responseBody = """
            {
              "content": [{"type": "text", "text": "{\"answerable\": true, \"answer\": \"You logged 3 entries.\", \"statsQueried\": [\"category\"]}"}],
              "stop_reason": "end_turn"
            }
            """;
        var engine = BuildEngine(responseBody);

        var answer = await engine.AskAsync(SampleStats, "How many entries did I log?");

        Assert.Equal("You logged 3 entries.", answer.Text);
    }

    [Fact]
    public async Task AskAsync_WhenStopReasonIsNotEndTurn_ThrowsInsightResponseParseException()
    {
        var responseBody = """
            {
              "content": [{"type": "text", "text": ""}],
              "stop_reason": "refusal"
            }
            """;
        var engine = BuildEngine(responseBody);

        await Assert.ThrowsAsync<InsightResponseParseException>(
            () => engine.AskAsync(SampleStats, "How many entries did I log?"));
    }

    [Fact]
    public async Task ExtractRangeAsync_WhenStopReasonIsEndTurn_ReturnsParsedRange()
    {
        var responseBody = """
            {
              "content": [{"type": "text", "text": "{\"from\": \"2026-01-01\", \"to\": \"2026-01-31\"}"}],
              "stop_reason": "end_turn"
            }
            """;
        var engine = BuildEngine(responseBody);

        var range = await engine.ExtractRangeAsync("What happened in January?");

        Assert.Equal(new DateOnly(2026, 1, 1), range.From);
        Assert.Equal(new DateOnly(2026, 1, 31), range.To);
    }

    [Fact]
    public async Task ExtractRangeAsync_WhenStopReasonIsNotEndTurn_FallsBackToOpenRangeRatherThanThrowing()
    {
        var responseBody = """
            {
              "content": [{"type": "text", "text": ""}],
              "stop_reason": "refusal"
            }
            """;
        var engine = BuildEngine(responseBody);

        var range = await engine.ExtractRangeAsync("What happened last week?");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }
}
