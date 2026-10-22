namespace TuracoChorus.Adapters.Cognito.Tests;

/// <summary>
/// Verifies a real, currently-valid JWT against a real Cognito user pool over the network — no
/// fakes, no fixed signing keys. Excluded from the default test run (this project's own
/// `VSTestTestCaseFilter` keeps a plain `dotnet test` green without credentials); run these
/// specifically with `dotnet test --filter "Category=Integration"`.
///
/// Needs a real, unexpired token — Cognito access/id tokens expire in about an hour, and this
/// app client only supports SRP auth (no plain USER_PASSWORD_AUTH), so minting one isn't a single
/// API call. Simplest path: use whatever token-minting tooling your own app already has for the
/// pool, and export the token here as `TURACO_TEST_ACCESS_TOKEN` right before running this test.
///
/// The token is a real bearer credential — never log, print, or assert on its raw value. It's
/// scoped to one throwaway test account with no real data, and short-lived (~1hr), but a leaked
/// copy still lets a holder call this API as that user until it expires.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CognitoIdentityVerifierIntegrationTests
{
    [Fact]
    public async Task VerifyIdentityAsync_WithRealToken_ReturnsUserId()
    {
        var options = new CognitoIdentityVerifierOptions(
            UserPoolId: RequireEnv("TURACO_TEST_COGNITO_USER_POOL_ID"),
            Region: RequireEnv("TURACO_TEST_COGNITO_REGION"),
            AppClientId: RequireEnv("TURACO_TEST_COGNITO_APP_CLIENT_ID"),
            TokenType: Enum.Parse<CognitoTokenType>(
                Environment.GetEnvironmentVariable("TURACO_TEST_COGNITO_TOKEN_TYPE") ?? "AccessToken"));
        var token = RequireEnv("TURACO_TEST_ACCESS_TOKEN");

        var verifier = new CognitoIdentityVerifier(options);

        var userId = await verifier.VerifyIdentityAsync(token);

        Assert.False(string.IsNullOrWhiteSpace(userId));
    }

    private static string RequireEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Integration test requires environment variable \"{name}\" to be set.");
}
