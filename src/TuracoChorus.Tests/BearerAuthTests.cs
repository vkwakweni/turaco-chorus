using Microsoft.AspNetCore.Http;
using TuracoChorus.Auth;
using TuracoChorus.Core.Fakes;
using Xunit;

namespace TuracoChorus.Tests;

public sealed class BearerAuthTests
{
    private static HttpRequest RequestWithAuthorizationHeader(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers.Authorization = headerValue;
        }

        return context.Request;
    }

    [Fact]
    public async Task AuthenticateAsync_WithRegisteredCredential_ReturnsAuthSucceededWithTheCorrectUserId()
    {
        var identityVerifier = new FakeIdentityVerifier();

        // Adding a user to be recognised with credentials
        identityVerifier.Register("valid-token", "user-1");

        // Creater header with valid bearer token
        var request = RequestWithAuthorizationHeader("Bearer valid-token");

        // Checks if the user requesting is verified
        var result = await BearerAuth.AuthenticateAsync(request, identityVerifier);

        var succeeded = Assert.IsType<AuthSucceeded>(result);
        Assert.Equal("user-1", succeeded.UserId);
    }

    [Fact]
    public async Task AuthenticateAsync_WithNoAuthorizationHeader_ReturnsAuthFailed()
    {
        var identityVerifier = new FakeIdentityVerifier();
        var request = RequestWithAuthorizationHeader(headerValue: null);

        var result = await BearerAuth.AuthenticateAsync(request, identityVerifier);

        var failed = Assert.IsType<AuthFailed>(result);
        Assert.Equal("Missing Authorization header.", failed.Reason);
    }

    [Fact]
    public async Task AuthenticateAsync_WithHeaderMissingBearerPrefix_ReturnsAuthFailed()
    {
        var identityVerifier = new FakeIdentityVerifier();
        identityVerifier.Register("valid-token", "user-1");
        var request = RequestWithAuthorizationHeader("valid-token");

        var result = await BearerAuth.AuthenticateAsync(request, identityVerifier);

        var failed = Assert.IsType<AuthFailed>(result);
        Assert.Equal("Authorization header is not a Bearer token.", failed.Reason);
    }

    [Fact]
    public async Task AuthenticateAsync_WithUnregisteredCredential_ReturnsAuthFailed()
    {
        var identityVerifier = new FakeIdentityVerifier();
        // "unknown-token" is deliberately never registered
        var request = RequestWithAuthorizationHeader("Bearer unknown-token");

        var result = await BearerAuth.AuthenticateAsync(request, identityVerifier);

        var failed = Assert.IsType<AuthFailed>(result);
        Assert.Equal("Credential rejected: No user registered for credential 'unknown-token'.", failed.Reason);
    }
}
