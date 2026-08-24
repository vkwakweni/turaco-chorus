namespace TuracoChorus.Core.Ports;

/// <summary>Thrown by IInsightEngine.AskAsync when a question can't be answered from the given AggregateStats.</summary>
public sealed class QuestionNotAnsweredException(string message) : Exception(message);

/// <summary>Thrown by IInsightEngine implementations when the AI provider's response can't be parsed as the expected shape.</summary>
public sealed class InsightResponseParseException(string message) : Exception(message);
