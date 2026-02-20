using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Infrastructure.Services;
using saas.Modules.TenantAdmin.Entities;
using saas.Shared;

namespace saas.Modules.TenantAdmin.Services;

public class TenantAdminService : ITenantAdminService
{
    private readonly TenantDbContext _db;
    private readonly CoreDbContext _coreDb;
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _templateService;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IFeatureService _featureService;
    private readonly IEnumerable<IModule> _modules;

    public TenantAdminService(
        TenantDbContext db,
        CoreDbContext coreDb,
        UserManager<AppUser> userManager,
        IEmailService emailService,
        IEmailTemplateService templateService,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        IFeatureService featureService,
        IEnumerable<IModule> modules)
    {
        _db = db;
        _coreDb = coreDb;
        _userManager = userManager;
        _emailService = emailService;
        _templateService = templateService;
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _featureService = featureService;
        _modules = modules;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    public async Task<PaginatedList<UserListItem>> GetUsersAsync(int page = 1, int pageSize = 20)
    {
        var query = _db.Users.OrderBy(u => u.Email);
        var totalCount = await query.CountAsync();
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var result = new List<UserListItem>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new UserListItem
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                LastLoginAt = user.LastLoginAt,
                CreatedAt = user.CreatedAt,
                Roles = roles.ToList()
            });
        }

        return new PaginatedList<UserListItem>(result, totalCount, page, pageSize);
    }

    public async Task<InviteUserResult> InviteUserAsync(string email, string? roleId = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new InviteUserResult(false, "Email is required.");

        // Enforce plan max-users limit
        if (_tenantContext.TenantId.HasValue)
        {
            var tenant = await _coreDb.Tenants
                .Include(t => t.Plan)
                .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId.Value);

            if (tenant?.Plan?.MaxUsers is not null)
            {
                var currentUserCount = await _userManager.Users.CountAsync();
                if (currentUserCount >= tenant.Plan.MaxUsers.Value)
                    return new InviteUserResult(false, $"User limit reached for your plan (max {tenant.Plan.MaxUsers.Value}). Upgrade to invite more users.");
            }
        }

        // Check if user already exists in this tenant
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return new InviteUserResult(false, "A user with this email already exists.");

        // Check for existing pending invitation
        var existingInvite = await _db.Set<TeamInvitation>()
            .AnyAsync(i => i.Email == email && i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow);
        if (existingInvite)
            return new InviteUserResult(false, "An invitation is already pending for this email.");

        string? roleName = null;
        if (!string.IsNullOrEmpty(roleId))
        {
            var role = await _db.Roles.FindAsync(roleId);
            roleName = role?.Name;
        }

        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var invitation = new TeamInvitation
        {
            Id = Guid.NewGuid(),
            Email = email.Trim(),
            RoleId = roleId,
            RoleName = roleName,
            Token = token,
            InvitedByUserId = string.Empty,
            InvitedByEmail = null,
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _db.Set<TeamInvitation>().Add(invitation);
        await _db.SaveChangesAsync();

        // Send invitation email using TeamInvitation template
        var slug = _tenantContext.Slug;
        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null ? $"{request.Scheme}://{request.Host}" : "";
        var acceptUrl = $"{baseUrl}/{slug}/admin/invitation/accept?token={Uri.EscapeDataString(token)}";

        var htmlBody = _templateService.Render("TeamInvitation", new Dictionary<string, string>
        {
            ["InviterEmail"] = "A team member",
            ["TenantName"] = slug ?? "unknown",
            ["RoleName"] = "Member",
            ["AcceptUrl"] = acceptUrl,
            ["ExpiresAt"] = invitation.ExpiresAt.ToString("MMMM d, yyyy")
        });

        await _emailService.SendAsync(new EmailMessage(
            email,
            $"You've been invited to join {slug}",
            htmlBody));

        return new InviteUserResult(true);
    }

    public async Task<bool> DeactivateUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        user.IsActive = false;
        await _userManager.UpdateAsync(user);
        return true;
    }

    public async Task<bool> ActivateUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        user.IsActive = true;
        await _userManager.UpdateAsync(user);
        return true;
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    public async Task<List<RoleListItem>> GetRolesAsync()
    {
        var roles = await _db.Roles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return roles.Select(r => new RoleListItem
        {
            Id = r.Id,
            Name = r.Name ?? string.Empty,
            Description = r.Description,
            IsSystemRole = r.IsSystemRole,
            Permissions = r.RolePermissions.Select(rp => rp.Permission.Name).ToList()
        }).ToList();
    }

    public async Task<List<Permission>> GetPermissionsAsync()
    {
        var allPermissions = await _db.Permissions
            .OrderBy(p => p.Group).ThenBy(p => p.SortOrder)
            .ToListAsync();

        // Build set of permission groups that belong to modules with disabled features
        var enabledFeatures = await _featureService.GetEnabledFeaturesAsync();
        var enabledSet = new HashSet<string>(enabledFeatures, StringComparer.OrdinalIgnoreCase);

        var hiddenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in _modules)
        {
            // Modules with no features → always show their permissions
            if (module.Features.Count == 0) continue;

            // If none of the module's features are enabled, hide its permission groups
            var hasAnyEnabled = module.Features.Any(f => enabledSet.Contains(f.Key));
            if (!hasAnyEnabled)
            {
                foreach (var perm in module.Permissions)
                    hiddenGroups.Add(perm.Group);
            }
        }

        return allPermissions
            .Where(p => !hiddenGroups.Contains(p.Group))
            .ToList();
    }

    public async Task<bool> AssignRoleAsync(string userId, string roleId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var role = await _db.Roles.FindAsync(roleId);
        if (role?.Name is null) return false;

        var result = await _userManager.AddToRoleAsync(user, role.Name);
        return result.Succeeded;
    }

    public async Task<bool> RemoveRoleAsync(string userId, string roleId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var role = await _db.Roles.FindAsync(roleId);
        if (role?.Name is null) return false;

        var result = await _userManager.RemoveFromRoleAsync(user, role.Name);
        return result.Succeeded;
    }

    public async Task<RoleListItem?> CreateRoleAsync(string name, string? description)
    {
        var role = new AppRole
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Description = description,
            IsSystemRole = false
        };

        var result = await _db.Roles.AddAsync(role);
        await _db.SaveChangesAsync();

        return new RoleListItem
        {
            Id = role.Id,
            Name = role.Name ?? name,
            Description = description,
            IsSystemRole = false,
            Permissions = []
        };
    }

    public async Task<bool> UpdateRoleAsync(string roleId, string name, string? description)
    {
        var role = await _db.Roles.FindAsync(roleId);
        if (role is null || role.IsSystemRole) return false;

        role.Name = name;
        role.NormalizedName = name.ToUpperInvariant();
        role.Description = description;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteRoleAsync(string roleId)
    {
        var role = await _db.Roles.FindAsync(roleId);
        if (role is null || role.IsSystemRole) return false;

        // Check if any users are assigned
        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Count > 0) return false;

        // Remove role-permissions first
        var rps = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _db.RolePermissions.RemoveRange(rps);

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ToggleRolePermissionAsync(string roleId, Guid permissionId)
    {
        var existing = await _db.RolePermissions
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        if (existing is not null)
        {
            _db.RolePermissions.Remove(existing);
        }
        else
        {
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            });
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<string>> GetUserRoleIdsAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return [];

        var roleNames = await _userManager.GetRolesAsync(user);
        var roleIds = await _db.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();
        return roleIds;
    }

    public async Task<bool> SetUserRolesAsync(string userId, List<string> roleIds)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        // Remove all current roles
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

        // Add new roles
        if (roleIds.Count > 0)
        {
            var roleNames = await _db.Roles
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Name!)
                .ToListAsync();
            await _userManager.AddToRolesAsync(user, roleNames);
        }

        return true;
    }
}
