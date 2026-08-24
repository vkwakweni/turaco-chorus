using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

[assembly: InternalsVisibleTo("TuracoChorus.Adapters.Claude.Tests")]

namespace TuracoChorus.Adapters.Claude;

/// <summary>
/// Parses Claude's structured-JSON text responses into domain types. Deliberately has no
/// HttpClient dependency — pure given the raw response text, so it's testable against
/// hand-built strings with no real API call.
/// </summary>
internal static class ClaudeResponseParser
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
    /// QuestionNotAnsweredException if Claude itself reports the question isn't answerable.
    /// </summary>
    public static Answer ParseAnswer(string responseText, DateRange range)
    {
        var payload = TryDeserialize<AskResponsePayload>(responseText)
            ?? throw new InsightResponseParseException(
                "Claude's response could not be parsed as the expected JSON shape.");

        if (!payload.Answerable || payload.Answer is null)
        {
            throw new QuestionNotAnsweredException(
                "Claude determined this question can't be answered from the available aggregates.");
        }

        return new Answer(
            Text: payload.Answer,
            DataUsed: new DataUsed(StatsQueried: payload.StatsQueried ?? [], Range: range));
    }

    /// <summary>
    /// Allowlist, not a blocklist: only null or "end_turn" counts as normal completion —
    /// everything else throws InsightResponseParseException, including stop_reason values not
    /// explicitly known here (e.g. a future one Anthropic adds later). Known non-normal values
    /// include "refusal" (blocked by Claude's content classifier) and "max_tokens" (truncated
    /// before completion), but nothing needs to be added to this check as new ones surface —
    /// rejecting the unrecognized case by default is the point.
    /// </summary>
    public static void EnsureNormalCompletion(string? stopReason)
    {
        if (stopReason is not (null or "end_turn"))
        {
            throw new InsightResponseParseException(
                $"Claude did not complete a normal response (stop_reason: {stopReason}).");
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
    /// Claude is instructed to respond with bare JSON, but strips a markdown code fence
    /// if one shows up anyway, rather than failing on an otherwise-well-formed response.
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
