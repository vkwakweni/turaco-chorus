namespace TuracoChorus.Contracts;

public sealed record ConsentResponse(bool Granted, DateTimeOffset? GrantedAt);

public sealed record ConsentRequest(bool Granted);
