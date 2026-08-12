using TuracoChorus.Core.Models;

namespace TuracoChorus.Core.Ports;

public interface IConsentStore
{
    Task<ConsentRecord> GetAsync(string userId);
    Task<ConsentRecord> SetAsync(string userId, bool granted);
}
