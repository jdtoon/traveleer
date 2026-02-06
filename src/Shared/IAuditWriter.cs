using saas.Data.Audit;

namespace saas.Shared;

/// <summary>
/// Fire-and-forget audit trail writer. Writes to a background channel.
/// </summary>
public interface IAuditWriter
{
    ValueTask WriteAsync(AuditEntry entry);
}
