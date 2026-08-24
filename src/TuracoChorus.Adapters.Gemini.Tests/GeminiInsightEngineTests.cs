using System.Net;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.Gemini.Tests;

/// <summary>
/// Confirms GeminiInsightEngine's pieces are actually wired together correctly (HTTP response
/// parsing, the two completion-status checks, and delegation into GeminiResponseParser) — not
/// an exhaustive case-by-case sweep. Every blockReason/finishReason value and its exact failure
/// behavior is already covered atomically in GeminiResponseParserTests against
/// EnsurePromptNotBlocked/EnsureNormalCompletion directly, with no HTTP mocking needed there.
/// </summary>
public sealed class GeminiInsightEngineTests
{
    private static readonly AggregateStats SampleStats = new(
        SourceId: "user-1",
        Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
        TotalEntries: 3,
        Dimensions: [new Dimension("category", [new DimensionBucket("Category A", 3)])]);

    private static GeminiInsightEngine BuildEngine(string responseBody)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseBody);
        var httpClient = new HttpClient(handler);
        return new GeminiInsightEngine(new GeminiInsightEngineOptions(ApiKey: "test-key"), httpClient);
    }

    [Fact]
    public async Task AskAsync_WhenFinishReasonIsStop_ReturnsAnswer()
    {
        var responseBody = """
            {
              "candidates": [{
                "content": {"parts": [{"text": "{\"answerable\": true, \"answer\": \"You logged 3 entries.\", \"statsQueried\": [\"category\"]}"}]},
                "finishReason": "STOP"
              }]
            }
            """;
        var engine = BuildEngine(responseBody);

        var answer = await engine.AskAsync(SampleStats, "How many entries did I log?");

        Assert.Equal("You logged 3 entries.", answer.Text);
    }

    [Fact]
    public async Task AskAsync_WhenFinishReasonIsNotStop_ThrowsInsightResponseParseException()
    {
        var responseBody = """
            {
              "candidates": [{
                "content": {"parts": [{"text": ""}]},
                "finishReason": "SAFETY"
              }]
            }
            """;
        var engine = BuildEngine(responseBody);

        await Assert.ThrowsAsync<InsightResponseParseException>(
            () => engine.AskAsync(SampleStats, "How many entries did I log?"));
    }

    [Fact]
    public async Task AskAsync_WhenPromptIsBlocked_ThrowsInsightResponseParseException()
    {
        var responseBody = """
            {
              "promptFeedback": {"blockReason": "SAFETY"}
            }
            """;
        var engine = BuildEngine(responseBody);

        await Assert.ThrowsAsync<InsightResponseParseException>(
            () => engine.AskAsync(SampleStats, "How many entries did I log?"));
    }

    [Fact]
    public async Task ExtractRangeAsync_WhenFinishReasonIsStop_ReturnsParsedRange()
    {
        var responseBody = """
            {
              "candidates": [{
                "content": {"parts": [{"text": "{\"from\": \"2026-01-01\", \"to\": \"2026-01-31\"}"}]},
                "finishReason": "STOP"
              }]
            }
            """;
        var engine = BuildEngine(responseBody);

        var range = await engine.ExtractRangeAsync("What happened in January?");

        Assert.Equal(new DateOnly(2026, 1, 1), range.From);
        Assert.Equal(new DateOnly(2026, 1, 31), range.To);
    }

    [Fact]
    public async Task ExtractRangeAsync_WhenFinishReasonIsNotStop_FallsBackToOpenRangeRatherThanThrowing()
    {
        var responseBody = """
            {
              "candidates": [{
                "content": {"parts": [{"text": ""}]},
                "finishReason": "SAFETY"
              }]
            }
            """;
        var engine = BuildEngine(responseBody);

        var range = await engine.ExtractRangeAsync("What happened last week?");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }

    [Fact]
    public async Task ExtractRangeAsync_WhenPromptIsBlocked_FallsBackToOpenRangeRatherThanThrowing()
    {
        var responseBody = """
            {
              "promptFeedback": {"blockReason": "SAFETY"}
            }
            """;
        var engine = BuildEngine(responseBody);

        var range = await engine.ExtractRangeAsync("What happened last week?");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }
}
