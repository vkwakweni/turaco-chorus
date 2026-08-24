using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.Gemini.Tests;

public sealed class GeminiPromptsTests
{
    [Fact]
    public void BuildRangeExtractionSystemPrompt_IncludesTodaysDate()
    {
        var prompt = GeminiPrompts.BuildRangeExtractionSystemPrompt(new DateOnly(2026, 3, 15));

        Assert.Contains("2026-03-15", prompt);
    }

    [Fact]
    public void AnsweringSystemPrompt_InstructsNeutralReportingAndNoRecommendations()
    {
        Assert.Contains("neutrally", GeminiPrompts.AnsweringSystemPrompt);
        Assert.Contains("Do not recommend actions", GeminiPrompts.AnsweringSystemPrompt);
    }

    [Fact]
    public void AnsweringSystemPrompt_DoesNotTreatCategoryOrDateAsSpecial()
    {
        Assert.Contains("do not assume a dimension named", GeminiPrompts.AnsweringSystemPrompt);
    }

    [Fact]
    public void BuildAskUserMessage_IncludesTheQuestionVerbatim()
    {
        var stats = new AggregateStats("user-1", new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), 0, []);

        var message = GeminiPrompts.BuildAskUserMessage(stats, "How many entries did I log?");

        Assert.Contains("How many entries did I log?", message);
    }

    [Fact]
    public void BuildAskUserMessage_SerializesRangeDimensionsAndTotalEntries()
    {
        var stats = new AggregateStats(
            SourceId: "user-1",
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 3,
            Dimensions: [new Dimension("category", [new DimensionBucket("Category A", 2), new DimensionBucket("Category B", 1)])]);

        var message = GeminiPrompts.BuildAskUserMessage(stats, "irrelevant");

        Assert.Contains("\"from\":\"2026-01-01\"", message);
        Assert.Contains("\"to\":\"2026-01-31\"", message);
        Assert.Contains("\"totalEntries\":3", message);
        Assert.Contains("\"name\":\"category\"", message);
        Assert.Contains("\"value\":\"Category A\",\"count\":2", message);
    }

    [Fact]
    public void BuildAskUserMessage_DoesNotIncludeSourceId()
    {
        var stats = new AggregateStats("user-1-should-not-appear", new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), 0, []);

        var message = GeminiPrompts.BuildAskUserMessage(stats, "irrelevant");

        Assert.DoesNotContain("user-1-should-not-appear", message);
    }
}
