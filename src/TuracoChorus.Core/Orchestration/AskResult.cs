using TuracoChorus.Core.Models;

namespace TuracoChorus.Core.Orchestration;

public abstract record AskResult;

public sealed record AskAllowed(Answer Answer) : AskResult;

public sealed record AskDenied : AskResult;