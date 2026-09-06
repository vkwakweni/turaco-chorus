using TuracoChorus.Adapters.Claude;
using TuracoChorus.Adapters.Gemini;

namespace TuracoChorus.Tests;

/// <summary>
/// Guards ai-provider-adapters.md's "genuinely interchangeable, word-for-word identical system
/// prompts" claim: nothing else catches a future one-sided edit to either adapter's prompt.
/// </summary>
public sealed class PromptSymmetryTests
{
    [Fact]
    public void AnsweringSystemPrompt_IsIdenticalBetweenClaudeAndGemini()
    {
        Assert.Equal(ClaudePrompts.AnsweringSystemPrompt, GeminiPrompts.AnsweringSystemPrompt);
    }

    [Fact]
    public void BuildRangeExtractionSystemPrompt_IsIdenticalBetweenClaudeAndGemini()
    {
        var today = new DateOnly(2026, 3, 15);

        var claudePrompt = ClaudePrompts.BuildRangeExtractionSystemPrompt(today);
        var geminiPrompt = GeminiPrompts.BuildRangeExtractionSystemPrompt(today);

        Assert.Equal(claudePrompt, geminiPrompt);
    }
}
