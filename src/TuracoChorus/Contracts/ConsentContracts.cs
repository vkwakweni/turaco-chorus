namespace TuracoChorus.Contracts;

public sealed record ConsentResponse(bool Granted, DateOnly? GrantedAt);

public sealed record ConsentRequest(bool Granted);
