namespace TuracoChorus.Auth;

public abstract record AuthResult;

public sealed record AuthSucceeded(string UserId) : AuthResult;

public sealed record AuthFailed(string Reason) : AuthResult;
