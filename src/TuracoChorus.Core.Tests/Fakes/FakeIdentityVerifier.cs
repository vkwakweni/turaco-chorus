using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Tests.Fakes;

public sealed class FakeIdentityVerifier : IIdentityVerifier
{
    private readonly Dictionary<string, string> _userIdsByCredential = new();

    public void Register(string rawCredential, string userId)
        => _userIdsByCredential[rawCredential] = userId;

    public Task<string> VerifyIdentityAsync(string rawCredential)
        => _userIdsByCredential.TryGetValue(rawCredential, out var userId)
            ? Task.FromResult(userId)
            : throw new InvalidOperationException($"No user registered for credential '{rawCredential}'.");
}
