using saas.Data.Audit;
using saas.Shared;

namespace saas.Modules.SuperAdmin.Services;

public class SuperAdminAuditService : ISuperAdminAuditService
{
    private readonly AuditDbContext _auditDb;
    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SuperAdminAuditService> _logger;

    public SuperAdminAuditService(
        AuditDbContext auditDb,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SuperAdminAuditService> logger)
    {
        _auditDb = auditDb;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(SuperAdminAuditEntry entry)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var auditEntry = new AuditEntry
            {
                Source = "SuperAdmin",
                TenantSlug = null,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Action = entry.Action,
                UserId = _currentUser.UserId,
                UserEmail = _currentUser.Email ?? "super-admin",
                OldValues = entry.OldValues,
                NewValues = entry.NewValues ?? entry.Details,
                Timestamp = DateTime.UtcNow,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString()
            };

            _auditDb.AuditEntries.Add(auditEntry);
            await _auditDb.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write super admin audit entry for {Action} on {EntityType}:{EntityId}",
                entry.Action, entry.EntityType, entry.EntityId);
        }
    }

    public Task LogAsync(string action, string entityType, string entityId, string? details = null)
    {
        return LogAsync(new SuperAdminAuditEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
    }
}
