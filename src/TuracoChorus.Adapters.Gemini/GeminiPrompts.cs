using System.Text.Json;
using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.Gemini;

/// <summary>
/// The two fixed system prompts, adapter-supplied and never caller-supplied. Mirrors
/// ClaudePrompts's wording and JSON contract exactly — same ethics-by-design constraints
/// (neutral fact-reporting, no recommendations/nudging) and generic dimension description,
/// so GeminiInsightEngine is a drop-in alternative to ClaudeInsightEngine behind IInsightEngine.
/// generationConfig.responseMimeType already forces JSON output on this provider, but the
/// prompt still says so explicitly for clarity and defense-in-depth.
/// </summary>
internal static class GeminiPrompts
{
    public static string BuildRangeExtractionSystemPrompt(DateOnly today) =>
        "You determine what date range a question needs, based only on the question's wording — " +
        "you never see any of the user's actual data. " +
        "Respond with ONLY a JSON object of the exact shape " +
        "{\"from\": \"YYYY-MM-DD\" or null, \"to\": \"YYYY-MM-DD\" or null}. " +
        "Use null for either bound when the question doesn't specify or imply it — an open bound " +
        "means \"earliest available\" or \"latest available\" data, not an error. " +
        $"Today's date is {today:yyyy-MM-dd}. Resolve relative phrases (\"last week\", \"this month\", " +
        "\"in March\") against it. If the question implies no particular date range at all, respond " +
        "with {\"from\": null, \"to\": null}. Do not include any text outside the JSON object — no " +
        "explanation, no markdown code fences.";

    public const string AnsweringSystemPrompt =
        "You answer questions about a user's own log data using only aggregated statistics provided " +
        "to you — never any raw entry text, which you are never given. " +
        "The data is organized into \"dimensions\" — an installer-defined breakdown of the user's " +
        "entries. Each dimension has a name and a list of buckets, each bucket a value and a count " +
        "of how many entries fall under it. Dimension names and values are entirely defined by " +
        "whoever configured this instance — do not assume a dimension named \"category\" or \"date\" " +
        "means something specific beyond what its own name and bucket values literally say. " +
        "Report facts neutrally: state what the data shows, using the user's own numbers and " +
        "dimension/bucket names. Do not recommend actions, suggest changes, or imply the user's data " +
        "\"should\" look any particular way — you are reporting, not advising. " +
        "If the question can't be answered from the given data — it asks about something no " +
        "dimension captures, or needs information outside what's provided — say so via the " +
        "\"answerable\" field rather than guessing. " +
        "Respond with ONLY a JSON object of the exact shape {\"answerable\": true or false, " +
        "\"answer\": string or null, \"statsQueried\": array of the dimension names you actually " +
        "drew on, or null}. When answerable is false, answer and statsQueried may be null. Do not " +
        "include any text outside the JSON object — no explanation, no markdown code fences.";

    public static string BuildAskUserMessage(AggregateStats stats, string question)
    {
        var payload = new
        {
            range = new
            {
                from = stats.Range.From.ToString("yyyy-MM-dd"),
                to = stats.Range.To.ToString("yyyy-MM-dd"),
            },
            totalEntries = stats.TotalEntries,
            dimensions = stats.Dimensions.Select(dimension => new
            {
                name = dimension.Name,
                buckets = dimension.Buckets.Select(bucket => new { value = bucket.Value, count = bucket.Count }),
            }),
        };

        var dataJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $"Question: {question}\n\nData:\n{dataJson}";
    }
}
