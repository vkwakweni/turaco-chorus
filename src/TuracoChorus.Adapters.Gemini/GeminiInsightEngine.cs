using System.Net.Http.Json;
using System.Text.Json;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.Gemini;

/// <summary>
/// Wraps the Google Gemini API (generateContent) to implement IInsightEngine — a free-tier
/// alternative to ClaudeInsightEngine behind the same port, usable without any Claude API
/// credits. generationConfig.responseMimeType = "application/json" gets structured output
/// natively, rather than relying only on prompt instructions the way the Claude adapter does.
/// </summary>
public sealed class GeminiInsightEngine(GeminiInsightEngineOptions options, HttpClient httpClient) : IInsightEngine
{
    private const int MaxOutputTokens = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RequestedRange> ExtractRangeAsync(string question)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            var responseText = await GenerateContentAsync(GeminiPrompts.BuildRangeExtractionSystemPrompt(today), question);
            return GeminiResponseParser.ParseRange(responseText);
        }
        catch (InsightResponseParseException)
        {
            // Keeps "ExtractRangeAsync always succeeds" true even when Gemini blocks or
            // truncates this specific call, not just when its response fails to parse.
            return new RequestedRange(null, null);
        }
    }

    public async Task<Answer> AskAsync(AggregateStats stats, string question)
    {
        var userMessage = GeminiPrompts.BuildAskUserMessage(stats, question);
        var responseText = await GenerateContentAsync(GeminiPrompts.AnsweringSystemPrompt, userMessage);
        return GeminiResponseParser.ParseAnswer(responseText, stats.Range);
    }

    private async Task<string> GenerateContentAsync(string systemPrompt, string userMessage)
    {
        var request = new GenerateContentRequest(
            Contents: [new GeminiContent("user", [new GeminiPart(userMessage)])],
            SystemInstruction: new SystemInstruction([new GeminiPart(systemPrompt)]),
            GenerationConfig: new GenerationConfig(ResponseMimeType: "application/json", MaxOutputTokens: MaxOutputTokens));

        var url = $"{options.BaseUrl.TrimEnd('/')}/v1beta/models/{options.Model}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        httpRequest.Headers.Add("x-goog-api-key", options.ApiKey);

        using var httpResponse = await httpClient.SendAsync(httpRequest);
        httpResponse.EnsureSuccessStatusCode();

        var body = await httpResponse.Content.ReadFromJsonAsync<GenerateContentResponse>(JsonOptions)
            ?? throw new InsightResponseParseException("Gemini returned an empty response.");

        GeminiResponseParser.EnsurePromptNotBlocked(body.PromptFeedback?.BlockReason);

        var candidate = body.Candidates?.FirstOrDefault();
        GeminiResponseParser.EnsureNormalCompletion(candidate?.FinishReason);

        return candidate?.Content?.Parts.FirstOrDefault()?.Text
            ?? throw new InsightResponseParseException("Gemini's response contained no text content.");
    }
}
