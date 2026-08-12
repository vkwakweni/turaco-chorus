namespace TuracoChorus.Core.Ports;

public interface IIdentityVerifier
{
    Task<string> VerifyIdentityAsync(string rawCredential);
}
