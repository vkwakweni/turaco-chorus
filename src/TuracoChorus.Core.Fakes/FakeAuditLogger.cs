using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Core.Fakes;

public sealed class FakeAuditLogger : IAuditLogger
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    /// <summary>Every call, including ones that threw — so tests can prove a path writes exactly once.</summary>
    public int AttemptCount { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public Task RecordAuditEntryAsync(AuditEntry entry)
    {
        AttemptCount++;
        if (ExceptionToThrow is not null)
        {
            return Task.FromException(ExceptionToThrow);
        }

        _entries.Add(entry);
        return Task.CompletedTask;
    }
}
