using System.Text.Json.Serialization;

namespace TuracoChorus.Adapters.Claude;

internal sealed record MessagesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("system")] string System,
    [property: JsonPropertyName("messages")] IReadOnlyList<MessageParam> Messages);

internal sealed record MessageParam(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record MessagesResponse(
    [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock> Content,
    [property: JsonPropertyName("stop_reason")] string? StopReason);

internal sealed record ContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text);
