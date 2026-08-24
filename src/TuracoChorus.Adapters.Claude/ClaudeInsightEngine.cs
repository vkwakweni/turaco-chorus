using System.Net.Http.Json;
using System.Text.Json;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.Claude;

/// <summary>
/// Wraps the Anthropic Claude Messages API to implement IInsightEngine. No official Anthropic
/// .NET SDK exists, so this calls the HTTP API directly. Both calls request a strict JSON
/// response (see ClaudePrompts) so mapping into RequestedRange/Answer is reliable rather than
/// parsed from free text.
/// </summary>
public sealed class ClaudeInsightEngine(ClaudeInsightEngineOptions options, HttpClient httpClient) : IInsightEngine
{
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RequestedRange> ExtractRangeAsync(string question)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            var responseText = await SendMessageAsync(ClaudePrompts.BuildRangeExtractionSystemPrompt(today), question);
            return ClaudeResponseParser.ParseRange(responseText);
        }
        catch (InsightResponseParseException)
        {
            // Keeps "ExtractRangeAsync always succeeds" true even when Claude refuses or
            // truncates this specific call, not just when its response fails to parse.
            return new RequestedRange(null, null);
        }
    }

    public async Task<Answer> AskAsync(AggregateStats stats, string question)
    {
        var userMessage = ClaudePrompts.BuildAskUserMessage(stats, question);
        var responseText = await SendMessageAsync(ClaudePrompts.AnsweringSystemPrompt, userMessage);
        return ClaudeResponseParser.ParseAnswer(responseText, stats.Range);
    }

    private async Task<string> SendMessageAsync(string systemPrompt, string userMessage)
    {
        var request = new MessagesRequest(
            Model: options.Model,
            MaxTokens: MaxTokens,
            System: systemPrompt,
            Messages: [new MessageParam("user", userMessage)]);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        httpRequest.Headers.Add("x-api-key", options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);

        using var httpResponse = await httpClient.SendAsync(httpRequest);
        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadFromJsonAsync<MessagesResponse>(JsonOptions)
            ?? throw new InsightResponseParseException("Claude returned an empty response.");

        ClaudeResponseParser.EnsureNormalCompletion(body.StopReason);

        return body.Content.FirstOrDefault(block => block.Type == "text")?.Text
            ?? throw new InsightResponseParseException("Claude's response contained no text content block.");
    }
}
