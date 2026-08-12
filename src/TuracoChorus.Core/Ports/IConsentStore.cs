using TuracoChorus.Core.Models;

namespace TuracoChorus.Core.Ports;

public interface IConsentStore
{
    Task<ConsentRecord> GetConsentAsync(string userId);
    Task<ConsentRecord> SetConsentAsync(string userId, bool granted);
}
