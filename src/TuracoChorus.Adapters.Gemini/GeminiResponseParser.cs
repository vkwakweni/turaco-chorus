using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

[assembly: InternalsVisibleTo("TuracoChorus.Adapters.Gemini.Tests")]

namespace TuracoChorus.Adapters.Gemini;

/// <summary>
/// Parses Gemini's structured-JSON text responses into domain types. Deliberately has no
/// HttpClient dependency — pure given the raw response text, so it's testable against
/// hand-built strings with no real API call. Mirrors ClaudeResponseParser's contract exactly.
/// </summary>
internal static class GeminiResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Never throws — an unparseable or ambiguous response falls back to an open range,
    /// per this project's decision that ExtractRangeAsync always succeeds.
    /// </summary>
    public static RequestedRange ParseRange(string responseText)
    {
        var payload = TryDeserialize<ExtractedRangePayload>(responseText);
        return payload is null
            ? new RequestedRange(null, null)
            : new RequestedRange(ParseDateOrNull(payload.From), ParseDateOrNull(payload.To));
    }

    /// <summary>
    /// Throws InsightResponseParseException if the response is unparseable, or
    /// QuestionNotAnsweredException if Gemini itself reports the question isn't answerable.
    /// </summary>
    public static Answer ParseAnswer(string responseText, DateRange range)
    {
        var payload = TryDeserialize<AskResponsePayload>(responseText)
            ?? throw new InsightResponseParseException(
                "Gemini's response could not be parsed as the expected JSON shape.");

        if (!payload.Answerable || payload.Answer is null)
        {
            throw new QuestionNotAnsweredException(
                "Gemini determined this question can't be answered from the available aggregates.");
        }

        return new Answer(
            Text: payload.Answer,
            DataUsed: new DataUsed(StatsQueried: payload.StatsQueried ?? [], Range: range));
    }

    /// <summary>
    /// Allowlist, not a blocklist: only a null blockReason counts as unblocked — any value at
    /// all throws InsightResponseParseException, including ones not explicitly known here (e.g.
    /// a future one Google adds later). Known values include "SAFETY", "PROHIBITED_CONTENT",
    /// and "BLOCKLIST", but nothing needs to be added to this check as new ones surface —
    /// rejecting the unrecognized case by default is the point.
    /// </summary>
    public static void EnsurePromptNotBlocked(string? blockReason)
    {
        if (blockReason is not null)
        {
            throw new InsightResponseParseException($"Gemini blocked this prompt (blockReason: {blockReason}).");
        }
    }

    /// <summary>
    /// Allowlist, not a blocklist: only null or "STOP" counts as normal completion — everything
    /// else throws InsightResponseParseException, including finishReason values not explicitly
    /// known here (e.g. a future one Google adds later). Known non-normal values include
    /// "SAFETY"/"PROHIBITED_CONTENT"/"RECITATION" (blocked mid-generation) and "MAX_TOKENS"
    /// (truncated before completion), but nothing needs to be added to this check as new ones
    /// surface — rejecting the unrecognized case by default is the point.
    /// </summary>
    public static void EnsureNormalCompletion(string? finishReason)
    {
        if (finishReason is not (null or "STOP"))
        {
            throw new InsightResponseParseException(
                $"Gemini did not complete a normal response (finishReason: {finishReason}).");
        }
    }

    private static T? TryDeserialize<T>(string text) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(StripCodeFences(text), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// generationConfig.responseMimeType already forces bare JSON, but this strips a
    /// markdown code fence defensively if one shows up anyway.
    /// </summary>
    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var withoutOpeningFence = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return (closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex] : withoutOpeningFence).Trim();
    }

    private static DateOnly? ParseDateOrNull(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && DateOnly.TryParse(raw, out var date) ? date : null;

    private sealed record ExtractedRangePayload(
        [property: JsonPropertyName("from")] string? From,
        [property: JsonPropertyName("to")] string? To);

    private sealed record AskResponsePayload(
        [property: JsonPropertyName("answerable")] bool Answerable,
        [property: JsonPropertyName("answer")] string? Answer,
        [property: JsonPropertyName("statsQueried")] IReadOnlyList<string>? StatsQueried);
}
