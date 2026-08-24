using System.Text.Json.Serialization;

namespace TuracoChorus.Adapters.Gemini;

internal sealed record GenerateContentRequest(
    [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
    [property: JsonPropertyName("systemInstruction")] SystemInstruction SystemInstruction,
    [property: JsonPropertyName("generationConfig")] GenerationConfig GenerationConfig);

internal sealed record GeminiContent(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

internal sealed record GeminiPart([property: JsonPropertyName("text")] string Text);

internal sealed record SystemInstruction(
    [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

internal sealed record GenerationConfig(
    [property: JsonPropertyName("responseMimeType")] string ResponseMimeType,
    [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

internal sealed record GenerateContentResponse(
    [property: JsonPropertyName("candidates")] IReadOnlyList<Candidate>? Candidates,
    [property: JsonPropertyName("promptFeedback")] PromptFeedback? PromptFeedback);

/// <summary>Set when the whole prompt was blocked before any candidate was generated.</summary>
internal sealed record PromptFeedback([property: JsonPropertyName("blockReason")] string? BlockReason);

internal sealed record Candidate(
    [property: JsonPropertyName("content")] GeminiContent? Content,
    [property: JsonPropertyName("finishReason")] string? FinishReason);
