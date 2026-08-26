using TuracoChorus.Adapters.Cognito;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

internal static class CognitoOptionsReader
{
    public static CognitoIdentityVerifierOptions Read(IConfiguration configuration)
    {
        var options = new CognitoIdentityVerifierOptions(
            UserPoolId: RequireString(configuration, "Cognito:UserPoolId"),
            Region: RequireString(configuration, "Cognito:Region"),
            AppClientId: RequireString(configuration, "Cognito:AppClientId"),
            TokenType: RequireEnum<CognitoTokenType>(configuration, "Cognito:TokenType"));

        return configuration["Cognito:UserIdClaim"] is { Length: > 0 } claim
            ? options with { UserIdClaim = claim }
            : options;
    }
}
