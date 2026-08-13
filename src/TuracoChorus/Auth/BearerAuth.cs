using TuracoChorus.Core.Ports;

namespace TuracoChorus.Auth;

public static class BearerAuth
{
    public static async Task<string?> AuthenticateAsync(HttpRequest request, IIdentityVerifier identityVerifier)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return null;
        }

        var rawCredential = header["Bearer ".Length..];
        try
        {
            return await identityVerifier.VerifyIdentityAsync(rawCredential);
        }
        catch
        {
            return null;
        }
    }
}
