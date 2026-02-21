using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;

namespace saas.Modules.SuperAdmin.Services;

public class TenantInspectionService : ITenantInspectionService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantInspectionService> _logger;

    public TenantInspectionService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<TenantInspectionService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    // ── Database Info ────────────────────────────────────────────────────────

    public async Task<TenantDatabaseInfoModel?> GetDatabaseInfoAsync(string slug)
    {
        var dbPath = GetTenantDbPath(slug);
        if (!File.Exists(dbPath))
            return null;

        var fileInfo = new FileInfo(dbPath);
        var walInfo = new FileInfo(dbPath + "-wal");

        var model = new TenantDatabaseInfoModel
        {
            Slug = slug,
            FilePath = dbPath,
            SizeBytes = fileInfo.Length,
            WalSizeBytes = walInfo.Exists ? walInfo.Length : 0,
            LastModifiedUtc = fileInfo.LastWriteTimeUtc
        };

        // Query table info using raw SQLite connection
        await using var context = CreateTenantContext(slug);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync();

        var tableNames = new List<string>();
        while (await reader.ReadAsync())
            tableNames.Add(reader.GetString(0));

        await reader.CloseAsync();

        foreach (var table in tableNames)
        {
            try
            {
                await using var countCmd = connection.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                var count = (long)(await countCmd.ExecuteScalarAsync() ?? 0);
                model.Tables.Add(new TableInfo { Name = table, RowCount = count });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count rows in table {Table} for tenant {Slug}", table, slug);
                model.Tables.Add(new TableInfo { Name = table, RowCount = -1 });
            }
        }

        return model;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    public async Task<List<TenantUserInfo>> GetUsersAsync(string slug)
    {
        await using var context = CreateTenantContext(slug);
        if (!File.Exists(GetTenantDbPath(slug))) return [];

        var users = await context.Users
            .AsNoTracking()
            .Select(u => new TenantUserInfo
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                DisplayName = u.DisplayName,
                IsActive = u.IsActive,
                EmailConfirmed = u.EmailConfirmed,
                TwoFactorEnabled = u.TwoFactorEnabled,
                LastLoginAt = u.LastLoginAt,
                LockoutEnd = u.LockoutEnd
            })
            .OrderBy(u => u.Email)
            .ToListAsync();

        // Fetch roles per user
        foreach (var user in users)
        {
            user.Roles = await context.UserRoles
                .Where(ur => ur.UserId == user.Id)
                .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name ?? "")
                .ToListAsync();
        }

        return users;
    }

    // ── Active Sessions ──────────────────────────────────────────────────────

    public async Task<List<TenantSessionInfo>> GetActiveSessionsAsync(string slug)
    {
        await using var context = CreateTenantContext(slug);
        if (!File.Exists(GetTenantDbPath(slug))) return [];

        return await context.UserSessions
            .AsNoTracking()
            .Where(s => s.ExpiresAt > DateTime.UtcNow && !s.IsRevoked)
            .Join(context.Users, s => s.UserId, u => u.Id, (s, u) => new TenantSessionInfo
            {
                SessionId = s.Id.ToString().Substring(0, 8) + "...", // Truncated for display
                UserEmail = u.Email ?? string.Empty,
                DeviceInfo = s.DeviceInfo,
                IpAddress = s.IpAddress,
                CreatedAt = s.CreatedAt,
                LastActivityAt = s.LastActivityAt,
                ExpiresAt = s.ExpiresAt ?? DateTime.MaxValue
            })
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync();
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    public async Task<List<TenantRoleInfo>> GetRolesAsync(string slug)
    {
        await using var context = CreateTenantContext(slug);
        if (!File.Exists(GetTenantDbPath(slug))) return [];

        var roles = await context.Roles
            .AsNoTracking()
            .Select(r => new TenantRoleInfo
            {
                Id = r.Id,
                Name = r.Name ?? string.Empty,
                IsSystemRole = r.IsSystemRole
            })
            .OrderBy(r => r.Name)
            .ToListAsync();

        foreach (var role in roles)
        {
            role.UserCount = await context.UserRoles.CountAsync(ur => ur.RoleId == role.Id);
            role.Permissions = await context.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Join(context.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Key)
                .ToListAsync();
        }

        return roles;
    }

    // ── Data Counts ──────────────────────────────────────────────────────────

    public async Task<TenantDataCountsModel> GetDataCountsAsync(string slug)
    {
        await using var context = CreateTenantContext(slug);
        if (!File.Exists(GetTenantDbPath(slug)))
            return new TenantDataCountsModel { Slug = slug };

        return new TenantDataCountsModel
        {
            Slug = slug,
            Users = await context.Users.CountAsync(),
            Roles = await context.Roles.CountAsync(),
            Notes = await context.Notes.CountAsync(),
            NotificationsTotal = await context.Notifications.CountAsync(),
            NotificationsUnread = await context.Notifications.CountAsync(n => !n.IsRead),
            ActiveSessions = await context.UserSessions.CountAsync(s => s.ExpiresAt > DateTime.UtcNow && !s.IsRevoked),
            PendingInvitations = await context.TeamInvitations.CountAsync(i => i.Status == InvitationStatus.Pending)
        };
    }

    // ── Pending Invitations ──────────────────────────────────────────────────

    public async Task<List<TenantInvitationInfo>> GetPendingInvitationsAsync(string slug)
    {
        await using var context = CreateTenantContext(slug);
        if (!File.Exists(GetTenantDbPath(slug))) return [];

        return await context.TeamInvitations
            .AsNoTracking()
            .Where(i => i.Status == InvitationStatus.Pending)
            .Join(context.Roles, i => i.RoleId, r => r.Id, (i, r) => new { Invitation = i, RoleName = r.Name ?? "" })
            .Select(x => new TenantInvitationInfo
            {
                Email = x.Invitation.Email,
                RoleName = x.RoleName,
                Status = x.Invitation.Status.ToString(),
                InvitedByEmail = x.Invitation.InvitedByEmail,
                SentAt = x.Invitation.CreatedAt,
                ExpiresAt = x.Invitation.ExpiresAt
            })
            .OrderByDescending(i => i.SentAt)
            .ToListAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetTenantDbPath(string slug)
    {
        var basePath = _configuration["Tenancy:DatabasePath"] ?? "db/tenants";
        return Path.Combine(basePath, $"{slug}.db");
    }

    private TenantDbContext CreateTenantContext(string slug)
    {
        var dbPath = GetTenantDbPath(slug);
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={dbPath};Mode=ReadOnly")
            .Options;
        return new TenantDbContext(options);
    }
}
