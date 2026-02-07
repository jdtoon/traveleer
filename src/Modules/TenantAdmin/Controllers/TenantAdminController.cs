using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.TenantAdmin.Services;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
public class TenantAdminController : SwapController
{
    private readonly ITenantAdminService _service;

    public TenantAdminController(ITenantAdminService service)
    {
        _service = service;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [HttpGet]
    [HasPermission(TenantAdminPermissions.UsersRead)]
    public async Task<IActionResult> Users(int page = 1)
    {
        var users = await _service.GetUsersAsync(page);
        return SwapView(users);
    }

    [HttpGet]
    [HasPermission(TenantAdminPermissions.UsersRead)]
    public async Task<IActionResult> UserList(int page = 1)
    {
        var users = await _service.GetUsersAsync(page);
        return SwapView("_UserList", users);
    }

    [HttpGet]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public IActionResult InviteUser()
    {
        return SwapView("_InviteUserModal");
    }

    [HttpPost]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public async Task<IActionResult> InviteUser([FromForm] string email)
    {
        var success = await _service.InviteUserAsync(email);
        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("User already exists or invalid email")
                .WithView("_InviteUserModal")
                .Build();
        }

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView("_ModalClose")
            .AlsoUpdate("user-list", "_UserList", users)
            .WithSuccessToast("Invitation sent!")
            .Build();
    }

    [HttpPost]
    [HasPermission(TenantAdminPermissions.UsersEdit)]
    public async Task<IActionResult> DeactivateUser(string id)
    {
        var success = await _service.DeactivateUserAsync(id);
        if (!success) return NotFound();

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView("_UserList", users)
            .WithWarningToast("User deactivated")
            .Build();
    }

    [HttpPost]
    [HasPermission(TenantAdminPermissions.UsersEdit)]
    public async Task<IActionResult> ActivateUser(string id)
    {
        var success = await _service.ActivateUserAsync(id);
        if (!success) return NotFound();

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView("_UserList", users)
            .WithSuccessToast("User activated")
            .Build();
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    [HttpGet]
    [HasPermission(TenantAdminPermissions.RolesRead)]
    public async Task<IActionResult> Roles()
    {
        var roles = await _service.GetRolesAsync();
        return SwapView(roles);
    }

    [HttpGet]
    [HasPermission(TenantAdminPermissions.RolesRead)]
    public async Task<IActionResult> RoleDetail(string id)
    {
        var roles = await _service.GetRolesAsync();
        var role = roles.FirstOrDefault(r => r.Id == id);
        if (role is null) return NotFound();

        var permissions = await _service.GetPermissionsAsync();
        return SwapView("_RoleDetail", new RoleDetailViewModel { Role = role, AllPermissions = permissions });
    }

    [HttpPost]
    [HasPermission(TenantAdminPermissions.RolesEdit)]
    public async Task<IActionResult> AssignRole([FromForm] string userId, [FromForm] string roleId)
    {
        var success = await _service.AssignRoleAsync(userId, roleId);
        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("Failed to assign role")
                .Build();
        }

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView("_UserList", users)
            .WithSuccessToast("Role assigned")
            .Build();
    }

    [HttpPost]
    [HasPermission(TenantAdminPermissions.RolesEdit)]
    public async Task<IActionResult> RemoveRole([FromForm] string userId, [FromForm] string roleId)
    {
        var success = await _service.RemoveRoleAsync(userId, roleId);
        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("Failed to remove role")
                .Build();
        }

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView("_UserList", users)
            .WithSuccessToast("Role removed")
            .Build();
    }
}

public class RoleDetailViewModel
{
    public RoleListItem Role { get; set; } = null!;
    public List<saas.Data.Tenant.Permission> AllPermissions { get; set; } = [];
}
