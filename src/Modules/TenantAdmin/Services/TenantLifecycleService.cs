using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Modules.Tenancy.Entities;
using saas.Shared;

namespace saas.Modules.TenantAdmin.Services;

public interface ITenantLifecycleService
{
    Task<byte[]> ExportTenantDataAsync();
    Task<bool> RequestDeletionAsync(int gracePeriodDays = 30);
    Task<bool> CancelDeletionAsync();
    Task<bool> PermanentlyDeleteTenantAsync(Guid tenantId);
}

public class TenantLifecycleService : ITenantLifecycleService
{
    private readonly CoreDbContext _coreDb;
    private readonly TenantDbContext _tenantDb;
    private readonly ITenantContext _tenantContext;

    public TenantLifecycleService(CoreDbContext coreDb, TenantDbContext tenantDb, ITenantContext tenantContext)
    {
        _coreDb = coreDb;
        _tenantDb = tenantDb;
        _tenantContext = tenantContext;
    }

    public async Task<byte[]> ExportTenantDataAsync()
    {
        var tenant = await _coreDb.Tenants
            .Include(t => t.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return [];

        // Export users from tenant DB
        var users = await _tenantDb.Users
            .AsNoTracking()
            .Select(u => new
            {
                u.Email,
                u.DisplayName,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt
            })
            .ToListAsync();

        // Export notes
        var notes = await _tenantDb.Notes
            .AsNoTracking()
            .Select(n => new
            {
                n.Title,
                n.Content,
                n.CreatedAt,
                n.UpdatedAt,
                CreatedBy = n.CreatedBy ?? "unknown"
            })
            .ToListAsync();

        // Export sessions
        var sessions = await _tenantDb.Set<saas.Modules.Auth.Entities.UserSession>()
            .AsNoTracking()
            .Select(s => new
            {
                s.UserId,
                s.IpAddress,
                s.DeviceInfo,
                s.CreatedAt,
                s.LastActivityAt,
                IsRevoked = s.IsRevoked
            })
            .ToListAsync();

        // Export notifications
        var notifications = await _tenantDb.Set<Notification>()
            .AsNoTracking()
            .Select(n => new
            {
                n.Title,
                n.Message,
                n.Type,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        // Export team invitations
        var invitations = await _tenantDb.Set<Entities.TeamInvitation>()
            .AsNoTracking()
            .Select(i => new
            {
                i.Email,
                Status = i.Status.ToString(),
                i.RoleName,
                i.InvitedByEmail,
                i.CreatedAt,
                i.ExpiresAt
            })
            .ToListAsync();

        var export = new
        {
            ExportedAt = DateTime.UtcNow,
            Tenant = new
            {
                tenant.Name,
                tenant.Slug,
                tenant.ContactEmail,
                Status = tenant.Status.ToString(),
                Plan = tenant.Plan?.Name,
                tenant.CreatedAt
            },
            Users = users,
            UserCount = users.Count,
            Notes = notes,
            Sessions = sessions,
            Notifications = notifications,
            Invitations = invitations
        };

        return JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public async Task<bool> RequestDeletionAsync(int gracePeriodDays = 30)
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return false;

        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.ScheduledDeletionAt = DateTime.UtcNow.AddDays(gracePeriodDays);
        await _coreDb.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CancelDeletionAsync()
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return false;

        tenant.IsDeleted = false;
        tenant.DeletedAt = null;
        tenant.ScheduledDeletionAt = null;
        await _coreDb.SaveChangesAsync();

        return true;
    }

    public async Task<bool> PermanentlyDeleteTenantAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return false;

        // Delete subscriptions, invoices, payments
        var subscriptions = await _coreDb.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .ToListAsync();
        _coreDb.Subscriptions.RemoveRange(subscriptions);

        var invoices = await _coreDb.Invoices
            .Where(i => i.TenantId == tenantId)
            .ToListAsync();
        _coreDb.Invoices.RemoveRange(invoices);

        var payments = await _coreDb.Payments
            .Where(p => p.TenantId == tenantId)
            .ToListAsync();
        _coreDb.Payments.RemoveRange(payments);

        // Delete the tenant itself
        _coreDb.Tenants.Remove(tenant);
        await _coreDb.SaveChangesAsync();

        // Delete the tenant SQLite database file and related WAL/SHM files
        var dbPath = Path.Combine("db", "tenants", $"{tenant.Slug}.db");
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + ext;
            if (File.Exists(path)) File.Delete(path);
        }

        return true;
    }
}
