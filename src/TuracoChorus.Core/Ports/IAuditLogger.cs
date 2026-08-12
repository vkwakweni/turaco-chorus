using TuracoChorus.Core.Models;

namespace TuracoChorus.Core.Ports;

public interface IAuditLogger
{
    Task RecordAsync(AuditEntry entry);
}
