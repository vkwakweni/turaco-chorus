namespace TuracoChorus.Core.Ports;

public interface IIdentityVerifier
{
    Task<string> VerifyAsync(string rawCredential);
}
