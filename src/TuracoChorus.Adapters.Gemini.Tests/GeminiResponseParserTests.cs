using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.Gemini.Tests;

public sealed class GeminiResponseParserTests
{
    [Fact]
    public void ParseRange_WithBothBoundsPresent_ReturnsThem()
    {
        var range = GeminiResponseParser.ParseRange("""{"from": "2026-01-01", "to": "2026-01-31"}""");

        Assert.Equal(new DateOnly(2026, 1, 1), range.From);
        Assert.Equal(new DateOnly(2026, 1, 31), range.To);
    }

    [Fact]
    public void ParseRange_WithBothBoundsNull_ReturnsOpenRange()
    {
        var range = GeminiResponseParser.ParseRange("""{"from": null, "to": null}""");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }

    [Fact]
    public void ParseRange_WrappedInMarkdownCodeFence_StillParses()
    {
        var range = GeminiResponseParser.ParseRange("```json\n{\"from\": \"2026-02-01\", \"to\": \"2026-02-28\"}\n```");

        Assert.Equal(new DateOnly(2026, 2, 1), range.From);
        Assert.Equal(new DateOnly(2026, 2, 28), range.To);
    }

    [Fact]
    public void ParseRange_WithUnparseableText_FallsBackToOpenRangeRatherThanThrowing()
    {
        var range = GeminiResponseParser.ParseRange("Sorry, I'm not sure what date range this needs.");

        Assert.Null(range.From);
        Assert.Null(range.To);
    }

    [Fact]
    public void ParseRange_WithInvalidDateString_TreatsThatBoundAsNull()
    {
        var range = GeminiResponseParser.ParseRange("""{"from": "not-a-date", "to": "2026-01-31"}""");

        Assert.Null(range.From);
        Assert.Equal(new DateOnly(2026, 1, 31), range.To);
    }

    private static readonly DateRange SampleRange = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

    [Fact]
    public void ParseAnswer_WhenAnswerable_ReturnsAnswerWithGivenRange()
    {
        var answer = GeminiResponseParser.ParseAnswer(
            """{"answerable": true, "answer": "You logged 3 entries.", "statsQueried": ["category"]}""",
            SampleRange);

        Assert.Equal("You logged 3 entries.", answer.Text);
        Assert.Equal(["category"], answer.DataUsed.StatsQueried);
        Assert.Same(SampleRange, answer.DataUsed.Range);
    }

    [Fact]
    public void ParseAnswer_WhenStatsQueriedOmitted_DefaultsToEmptyList()
    {
        var answer = GeminiResponseParser.ParseAnswer(
            """{"answerable": true, "answer": "You logged 3 entries."}""",
            SampleRange);

        Assert.Empty(answer.DataUsed.StatsQueried);
    }

    [Fact]
    public void ParseAnswer_WhenNotAnswerable_ThrowsQuestionNotAnsweredException()
    {
        Assert.Throws<QuestionNotAnsweredException>(() => GeminiResponseParser.ParseAnswer(
            """{"answerable": false, "answer": null, "statsQueried": null}""",
            SampleRange));
    }

    [Fact]
    public void ParseAnswer_WhenAnswerableButAnswerIsNull_ThrowsQuestionNotAnsweredException()
    {
        Assert.Throws<QuestionNotAnsweredException>(() => GeminiResponseParser.ParseAnswer(
            """{"answerable": true, "answer": null}""",
            SampleRange));
    }

    [Fact]
    public void ParseAnswer_WithUnparseableText_ThrowsInsightResponseParseException()
    {
        Assert.Throws<InsightResponseParseException>(
            () => GeminiResponseParser.ParseAnswer("not json at all", SampleRange));
    }

    [Fact]
    public void EnsurePromptNotBlocked_WithNullBlockReason_DoesNotThrow()
    {
        var exception = Record.Exception(() => GeminiResponseParser.EnsurePromptNotBlocked(null));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("PROHIBITED_CONTENT")]
    [InlineData("BLOCKLIST")]
    public void EnsurePromptNotBlocked_WithBlockReasonSet_ThrowsInsightResponseParseException(string blockReason)
    {
        var ex = Assert.Throws<InsightResponseParseException>(
            () => GeminiResponseParser.EnsurePromptNotBlocked(blockReason));
        Assert.Contains(blockReason, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("STOP")]
    public void EnsureNormalCompletion_WithNormalFinishReason_DoesNotThrow(string? finishReason)
    {
        var exception = Record.Exception(() => GeminiResponseParser.EnsureNormalCompletion(finishReason));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("RECITATION")]
    [InlineData("MAX_TOKENS")]
    public void EnsureNormalCompletion_WithNonNormalFinishReason_ThrowsInsightResponseParseException(string finishReason)
    {
        var ex = Assert.Throws<InsightResponseParseException>(
            () => GeminiResponseParser.EnsureNormalCompletion(finishReason));
        Assert.Contains(finishReason, ex.Message);
    }
}
