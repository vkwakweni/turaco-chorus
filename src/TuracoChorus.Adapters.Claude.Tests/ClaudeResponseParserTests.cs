using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.Claude.Tests;

public sealed class ClaudeResponseParserTests
{
    [Fact]
    public void ParseRange_WithBothBoundsPresent_ReturnsThem()
    {
        var range = ClaudeResponseParser.ParseRange("""{"from": "2026-01-01", "to": "2026-01-31"}""");

        Assert.Equal(new DateOnly(2026, 1, 1), range.From);
        Assert.Equal(new DateOnly(2026, 1, 31), range.To);
    }

    [Fact]
    public void ParseRange_WithBothBoundsNull_ReturnsOpenRange()
    {
        var range = ClaudeResponseParser.ParseRange("""{"from": null, "to": null}""");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }

    [Fact]
    public void ParseRange_WrappedInMarkdownCodeFence_StillParses()
    {
        var range = ClaudeResponseParser.ParseRange("```json\n{\"from\": \"2026-02-01\", \"to\": \"2026-02-28\"}\n```");

        Assert.Equal(new DateOnly(2026, 2, 1), range.From);
        Assert.Equal(new DateOnly(2026, 2, 28), range.To);
    }

    [Fact]
    public void ParseRange_WithUnparseableText_FallsBackToOpenRangeRatherThanThrowing()
    {
        var range = ClaudeResponseParser.ParseRange("Sorry, I'm not sure what date range this needs.");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }

    [Fact]
    public void ParseRange_WithInvalidDateString_TreatsThatBoundAsNull()
    {
        var range = ClaudeResponseParser.ParseRange("""{"from": "not-a-date", "to": "2026-01-31"}""");

        Assert.Null(range.From);
        Assert.Equal(new DateOnly(2026, 1, 31), range.To);
    }

    private static readonly DateRange SampleRange = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

    [Fact]
    public void ParseAnswer_WhenAnswerable_ReturnsAnswerWithGivenRange()
    {
        var answer = ClaudeResponseParser.ParseAnswer(
            """{"answerable": true, "answer": "You logged 3 entries.", "statsQueried": ["category"]}""",
            SampleRange);

        Assert.Equal("You logged 3 entries.", answer.Text);
        Assert.Equal(["category"], answer.DataUsed.StatsQueried);
        Assert.Same(SampleRange, answer.DataUsed.Range);
    }

    [Fact]
    public void ParseAnswer_WhenStatsQueriedOmitted_DefaultsToEmptyList()
    {
        var answer = ClaudeResponseParser.ParseAnswer(
            """{"answerable": true, "answer": "You logged 3 entries."}""",
            SampleRange);

        Assert.Empty(answer.DataUsed.StatsQueried);
    }

    [Fact]
    public void ParseAnswer_WhenNotAnswerable_ThrowsQuestionNotAnsweredException()
    {
        Assert.Throws<QuestionNotAnsweredException>(() => ClaudeResponseParser.ParseAnswer(
            """{"answerable": false, "answer": null, "statsQueried": null}""",
            SampleRange));
    }

    [Fact]
    public void ParseAnswer_WhenAnswerableButAnswerIsNull_ThrowsQuestionNotAnsweredException()
    {
        Assert.Throws<QuestionNotAnsweredException>(() => ClaudeResponseParser.ParseAnswer(
            """{"answerable": true, "answer": null}""",
            SampleRange));
    }

    [Fact]
    public void ParseAnswer_WithUnparseableText_ThrowsInsightResponseParseException()
    {
        Assert.Throws<InsightResponseParseException>(
            () => ClaudeResponseParser.ParseAnswer("not json at all", SampleRange));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("end_turn")]
    public void EnsureNormalCompletion_WithNormalStopReason_DoesNotThrow(string? stopReason)
    {
        var exception = Record.Exception(() => ClaudeResponseParser.EnsureNormalCompletion(stopReason));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    [InlineData("stop_sequence")]
    public void EnsureNormalCompletion_WithNonNormalStopReason_ThrowsInsightResponseParseException(string stopReason)
    {
        var ex = Assert.Throws<InsightResponseParseException>(
            () => ClaudeResponseParser.EnsureNormalCompletion(stopReason));
        Assert.Contains(stopReason, ex.Message);
    }
}
