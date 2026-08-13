namespace TuracoChorus.Contracts;

public sealed record AskRequest(string Question);

public sealed record DataUsedResponse(IReadOnlyList<string> StatsQueried, DateRangeResponse Range);

public sealed record AnswerResponse(string Answer, DataUsedResponse DataUsed);
