using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Fakes;

public sealed class FakeAuditLogger : IAuditLogger
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public Task RecordAuditEntryAsync(AuditEntry entry)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }
}
