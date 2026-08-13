using TuracoChorus.Core.Ports;

namespace TuracoChorus.Auth;

public static class BearerAuth
{
    public static async Task<AuthResult> AuthenticateAsync(HttpRequest request, IIdentityVerifier identityVerifier)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return new AuthFailed("Missing Authorization header.");
        }

        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return new AuthFailed("Authorization header is not a Bearer token.");
        }

        var rawCredential = header["Bearer ".Length..];
        try
        {
            var userId = await identityVerifier.VerifyIdentityAsync(rawCredential);
            return new AuthSucceeded(userId);
        }
        catch (Exception ex)
        {
            return new AuthFailed($"Credential rejected: {ex.Message}");
        }
    }
}
