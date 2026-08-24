using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Fakes;

public sealed class FakeConsentStore : IConsentStore
{
    private readonly Dictionary<string, ConsentRecord> _records = new();

    public Task<ConsentRecord> GetConsentAsync(string userId)
    {
        if (_records.TryGetValue(userId, out var record))
        {
            return Task.FromResult(record);
        }

        return Task.FromResult(new ConsentRecord(userId, Granted: false, GrantedAt: null));
    }

    public Task<ConsentRecord> SetConsentAsync(string userId, bool granted)
    {
        var record = new ConsentRecord(userId, granted, DateTimeOffset.UtcNow);
        _records[userId] = record;
        return Task.FromResult(record);
    }
}
