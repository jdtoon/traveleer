using saas.Data.Audit;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class NullAuditWriter : IAuditWriter
{
    public ValueTask WriteAsync(AuditEntry entry)
    {
        return ValueTask.CompletedTask;
    }
}
