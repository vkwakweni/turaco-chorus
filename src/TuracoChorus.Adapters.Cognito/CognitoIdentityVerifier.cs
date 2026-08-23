using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TuracoChorus.Core.Ports;

[assembly: InternalsVisibleTo("TuracoChorus.Adapters.Cognito.Tests")]

namespace TuracoChorus.Adapters.Cognito;

/// <summary>
/// Verifies a raw Cognito JWT against a configured user pool, returning the verified user id.
/// Validation is manual (JwtSecurityTokenHandler), not ASP.NET Core's AddJwtBearer pipeline —
/// BearerAuth.cs calls VerifyIdentityAsync as a plain method and treats any thrown exception
/// as an auth failure, so no special exception type is needed here.
/// </summary>
public sealed class CognitoIdentityVerifier : IIdentityVerifier
{
    private readonly CognitoIdentityVerifierOptions _options;
    private readonly string _issuer;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public CognitoIdentityVerifier(CognitoIdentityVerifierOptions options)
        : this(options, BuildConfigurationManager(options))
    {
    }

    /// <summary>Test seam — lets tests supply a fixed signing-key set with no network calls.</summary>
    internal CognitoIdentityVerifier(
        CognitoIdentityVerifierOptions options,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        _options = options;
        _issuer = Issuer(options);
        _configurationManager = configurationManager;
    }

    private static string Issuer(CognitoIdentityVerifierOptions options)
        => $"https://cognito-idp.{options.Region}.amazonaws.com/{options.UserPoolId}";

    private static ConfigurationManager<OpenIdConnectConfiguration> BuildConfigurationManager(
        CognitoIdentityVerifierOptions options)
        => new(
            $"{Issuer(options)}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

    public async Task<string> VerifyIdentityAsync(string rawCredential)
    {
        var config = await _configurationManager.GetConfigurationAsync(CancellationToken.None);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
            // Audience is validated manually below: Cognito puts it in different claims
            // depending on token type (aud for ID tokens, client_id for access tokens).
            ValidateAudience = false,
        };

        var principal = _tokenHandler.ValidateToken(rawCredential, validationParameters, out var validatedToken);
        var jwt = (JwtSecurityToken)validatedToken;

        var expectedTokenUse = _options.TokenType == CognitoTokenType.IdToken ? "id" : "access";
        var actualTokenUse = jwt.Claims.FirstOrDefault(c => c.Type == "token_use")?.Value;
        if (actualTokenUse != expectedTokenUse)
        {
            throw new SecurityTokenException(
                $"Expected a Cognito {expectedTokenUse} token (token_use=\"{expectedTokenUse}\"), but got \"{actualTokenUse ?? "(none)"}\".");
        }

        var audienceClaimType = _options.TokenType == CognitoTokenType.IdToken ? "aud" : "client_id";
        var audience = jwt.Claims.FirstOrDefault(c => c.Type == audienceClaimType)?.Value;
        if (audience != _options.AppClientId)
        {
            throw new SecurityTokenException(
                $"Token's \"{audienceClaimType}\" claim does not match the configured AppClientId.");
        }

        var userId = jwt.Claims.FirstOrDefault(c => c.Type == _options.UserIdClaim)?.Value;
        if (userId is null)
        {
            throw new SecurityTokenException($"Token has no \"{_options.UserIdClaim}\" claim.");
        }

        return userId;
    }
}
