using TuracoChorus.Core.Orchestration;
using TuracoChorus.Core.Fakes;
using Xunit;

namespace TuracoChorus.Core.Tests.Orchestration;

public sealed class ConsentOrchestratorTests
{
    [Fact]
    public async Task GetConsentAsync_ReturnsRecordForTheRequestedUser_NotSomeoneElses()
    {
        // Create consent store
        var consentStore = new FakeConsentStore();

        // Create two users with distinguishable consent values
        await consentStore.SetConsentAsync("user-1", granted: true);
        await consentStore.SetConsentAsync("user-2", granted: false);

        var orchestrator = new ConsentOrchestrator(consentStore);

        // Get the consent value for one specific user
        var result = await orchestrator.GetConsentAsync("user-2");

        // Confirm it's the chosen user's consent data
        Assert.Equal("user-2", result.UserId);
        Assert.False(result.Granted);
    }

    [Fact]
    public async Task SetConsentAsync_ThenGetConsentAsync_ReturnsTheSameRecord()
    {
        var consentStore = new FakeConsentStore();
        var orchestrator = new ConsentOrchestrator(consentStore);

        var setResult = await orchestrator.SetConsentAsync("user-1", granted: true);
        var getResult = await orchestrator.GetConsentAsync("user-1");

        Assert.True(setResult.Granted);
        Assert.NotNull(setResult.GrantedAt);
        Assert.Equal(setResult, getResult);
    }
}
