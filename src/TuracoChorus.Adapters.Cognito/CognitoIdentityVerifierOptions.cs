namespace TuracoChorus.Adapters.Cognito;

public enum CognitoTokenType
{
    IdToken,
    AccessToken
}

public sealed record CognitoIdentityVerifierOptions(
    string UserPoolId,
    string Region,
    string AppClientId,
    CognitoTokenType TokenType,
    string UserIdClaim = "sub");
