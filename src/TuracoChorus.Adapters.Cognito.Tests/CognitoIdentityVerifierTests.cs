using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TuracoChorus.Adapters.Cognito.Tests;

public sealed class CognitoIdentityVerifierTests
{
    private const string UserPoolId = "us-east-1_TestPool";
    private const string Region = "us-east-1";
    private const string AppClientId = "test-app-client-id";
    private const string Issuer = $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";

    private readonly RSA _signingKey = RSA.Create(2048);

    private CognitoIdentityVerifier BuildVerifier(CognitoTokenType tokenType, string userIdClaim = "sub")
    {
        var options = new CognitoIdentityVerifierOptions(UserPoolId, Region, AppClientId, tokenType, userIdClaim);
        var configManager = new FixedConfigurationManager(new OpenIdConnectConfiguration
        {
            SigningKeys = { new RsaSecurityKey(_signingKey) },
        });
        return new CognitoIdentityVerifier(options, configManager);
    }

    private string SignToken(
        string tokenUse,
        string? audienceClaimValue,
        bool useAccessTokenAudienceClaim,
        string sub = "user-123",
        DateTime? expires = null,
        RSA? signWith = null)
    {
        var claims = new List<Claim>
        {
            new("token_use", tokenUse),
            new("sub", sub),
        };
        if (audienceClaimValue is not null)
        {
            claims.Add(new Claim(useAccessTokenAudienceClaim ? "client_id" : "aud", audienceClaimValue));
        }

        var credentials = new SigningCredentials(
            new RsaSecurityKey(signWith ?? _signingKey), SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithValidIdToken_ReturnsUserId()
    {
        var verifier = BuildVerifier(CognitoTokenType.IdToken);
        var token = SignToken("id", AppClientId, useAccessTokenAudienceClaim: false, sub: "user-abc");

        var userId = await verifier.VerifyIdentityAsync(token);

        Assert.Equal("user-abc", userId);
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithValidAccessToken_ReturnsUserId()
    {
        var verifier = BuildVerifier(CognitoTokenType.AccessToken);
        var token = SignToken("access", AppClientId, useAccessTokenAudienceClaim: true, sub: "user-xyz");

        var userId = await verifier.VerifyIdentityAsync(token);

        Assert.Equal("user-xyz", userId);
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithWrongTokenUse_Throws()
    {
        var verifier = BuildVerifier(CognitoTokenType.IdToken);
        var token = SignToken("access", AppClientId, useAccessTokenAudienceClaim: true);

        var ex = await Assert.ThrowsAsync<SecurityTokenException>(() => verifier.VerifyIdentityAsync(token));
        Assert.Contains("token_use", ex.Message);
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithWrongAudience_Throws()
    {
        var verifier = BuildVerifier(CognitoTokenType.IdToken);
        var token = SignToken("id", "some-other-client-id", useAccessTokenAudienceClaim: false);

        var ex = await Assert.ThrowsAsync<SecurityTokenException>(() => verifier.VerifyIdentityAsync(token));
        Assert.Contains("aud", ex.Message);
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithMissingUserIdClaim_Throws()
    {
        var verifier = BuildVerifier(CognitoTokenType.IdToken, userIdClaim: "email");
        var token = SignToken("id", AppClientId, useAccessTokenAudienceClaim: false);

        var ex = await Assert.ThrowsAsync<SecurityTokenException>(() => verifier.VerifyIdentityAsync(token));
        Assert.Contains("email", ex.Message);
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithExpiredToken_Throws()
    {
        var verifier = BuildVerifier(CognitoTokenType.IdToken);
        var token = SignToken(
            "id", AppClientId, useAccessTokenAudienceClaim: false, expires: DateTime.UtcNow.AddHours(-1));

        await Assert.ThrowsAsync<SecurityTokenExpiredException>(() => verifier.VerifyIdentityAsync(token));
    }

    [Fact]
    public async Task VerifyIdentityAsync_WithWrongSigningKey_Throws()
    {
        var verifier = BuildVerifier(CognitoTokenType.IdToken);
        using var wrongKey = RSA.Create(2048);
        var token = SignToken("id", AppClientId, useAccessTokenAudienceClaim: false, signWith: wrongKey);

        await Assert.ThrowsAsync<SecurityTokenSignatureKeyNotFoundException>(
            () => verifier.VerifyIdentityAsync(token));
    }

    /// <summary>A fixed, network-free stand-in for the real Cognito-fetching ConfigurationManager.</summary>
    private sealed class FixedConfigurationManager(OpenIdConnectConfiguration config)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
            => Task.FromResult(config);

        public void RequestRefresh()
        {
        }
    }
}
