using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Orchestration;

public sealed class ConsentOrchestrator(IConsentStore consentStore)
{
    public Task<ConsentRecord> GetConsentAsync(string userId)
        => consentStore.GetConsentAsync(userId);

    public Task<ConsentRecord> SetConsentAsync(string userId, bool granted)
        => consentStore.SetConsentAsync(userId, granted);
}