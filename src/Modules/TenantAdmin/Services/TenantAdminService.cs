using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Shared;

namespace saas.Modules.TenantAdmin.Services;

public class TenantAdminService : ITenantAdminService
{
    private readonly TenantDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ITenantContext _tenantContext;

    public TenantAdminService(
        TenantDbContext db,
        UserManager<AppUser> userManager,
        IEmailService emailService,
        ITenantContext tenantContext)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
        _tenantContext = tenantContext;
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

    public async Task<bool> InviteUserAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        // Check if user already exists in this tenant
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null) return false;

        // Create AppUser in tenant DB
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded) return false;

        // Send magic link invitation
        var slug = _tenantContext.Slug;
        var loginUrl = $"/{slug}/login";
        await _emailService.SendMagicLinkAsync(email, loginUrl);

        return true;
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
        return await _db.Permissions
            .OrderBy(p => p.Group).ThenBy(p => p.SortOrder)
            .ToListAsync();
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
}
