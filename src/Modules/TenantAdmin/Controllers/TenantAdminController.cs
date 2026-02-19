using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.TenantAdmin.Models;
using saas.Modules.TenantAdmin.Services;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
[Route("{slug}/admin")]
public class TenantAdminController : SwapController
{
    private readonly ITenantAdminService _service;

    public TenantAdminController(ITenantAdminService service)
    {
        _service = service;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    [HasPermission(TenantAdminPermissions.UsersRead)]
    public async Task<IActionResult> Users(int page = 1)
    {
        var users = await _service.GetUsersAsync(page);
        return SwapView(users);
    }

    [HttpGet("user-list")]
    [HasPermission(TenantAdminPermissions.UsersRead)]
    public async Task<IActionResult> UserList(int page = 1)
    {
        var users = await _service.GetUsersAsync(page);
        return SwapView(SwapViews.TenantAdmin._UserList, users);
    }

    [HttpGet("invite-user")]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public async Task<IActionResult> InviteUser()
    {
        var roles = await _service.GetRolesAsync();
        return SwapView(SwapViews.TenantAdmin._InviteUserModal, new InviteUserViewModel { AvailableRoles = roles });
    }

    [HttpPost("invite-user")]
    [HasPermission(TenantAdminPermissions.UsersCreate)]
    public async Task<IActionResult> InviteUser([FromForm] string email, [FromForm] string? roleId)
    {
        var result = await _service.InviteUserAsync(email, roleId);
        if (!result.Success)
        {
            var roles = await _service.GetRolesAsync();
            return SwapResponse()
                .WithErrorToast(result.Error ?? "Failed to invite user")
                .WithView(SwapViews.TenantAdmin._InviteUserModal, new InviteUserViewModel { AvailableRoles = roles })
                .Build();
        }

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._ModalClose)
            .AlsoUpdate(SwapElements.UserList, SwapViews.TenantAdmin._UserList, users)
            .WithSuccessToast("Invitation sent!")
            .Build();
    }

    [HttpPost("deactivate-user")]
    [HasPermission(TenantAdminPermissions.UsersEdit)]
    public async Task<IActionResult> DeactivateUser(string id)
    {
        var success = await _service.DeactivateUserAsync(id);
        if (!success) return NotFound();

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._UserList, users)
            .WithWarningToast("User deactivated")
            .Build();
    }

    [HttpPost("activate-user")]
    [HasPermission(TenantAdminPermissions.UsersEdit)]
    public async Task<IActionResult> ActivateUser(string id)
    {
        var success = await _service.ActivateUserAsync(id);
        if (!success) return NotFound();

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._UserList, users)
            .WithSuccessToast("User activated")
            .Build();
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    [HttpGet("roles")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesRead)]
    public async Task<IActionResult> Roles()
    {
        var roles = await _service.GetRolesAsync();
        return SwapView(roles);
    }

    [HttpGet("role-list")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesRead)]
    public async Task<IActionResult> RoleList()
    {
        var roles = await _service.GetRolesAsync();
        return SwapView(SwapViews.TenantAdmin._RoleList, roles);
    }

    [HttpGet("role-detail")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesRead)]
    public async Task<IActionResult> RoleDetail(string id)
    {
        var roles = await _service.GetRolesAsync();
        var role = roles.FirstOrDefault(r => r.Id == id);
        if (role is null) return NotFound();

        var permissions = await _service.GetPermissionsAsync();
        return SwapView(SwapViews.TenantAdmin._RoleDetail, new RoleDetailViewModel { Role = role, AllPermissions = permissions });
    }

    [HttpGet("create-role")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesCreate)]
    public IActionResult CreateRole()
    {
        return SwapView(SwapViews.TenantAdmin._CreateRoleModal);
    }

    [HttpPost("create-role")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesCreate)]
    public async Task<IActionResult> CreateRole([FromForm] string name, [FromForm] string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SwapResponse()
                .WithErrorToast("Role name is required")
                .WithView(SwapViews.TenantAdmin._CreateRoleModal)
                .Build();
        }

        var role = await _service.CreateRoleAsync(name, description);
        if (role is null)
        {
            return SwapResponse()
                .WithErrorToast("Failed to create role")
                .WithView(SwapViews.TenantAdmin._CreateRoleModal)
                .Build();
        }

        var roles = await _service.GetRolesAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._ModalClose)
            .AlsoUpdate(SwapElements.RoleList, SwapViews.TenantAdmin._RoleList, roles)
            .WithSuccessToast("Role created")
            .Build();
    }

    [HttpGet("edit-role")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesEdit)]
    public async Task<IActionResult> EditRole(string id)
    {
        var roles = await _service.GetRolesAsync();
        var role = roles.FirstOrDefault(r => r.Id == id);
        if (role is null) return NotFound();

        return SwapView(SwapViews.TenantAdmin._EditRoleModal, role);
    }

    [HttpPost("edit-role")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesEdit)]
    public async Task<IActionResult> EditRole([FromForm] string id, [FromForm] string name, [FromForm] string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return SwapResponse()
                .WithErrorToast("Role name is required")
                .Build();
        }

        var success = await _service.UpdateRoleAsync(id, name, description);
        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("Cannot edit system roles")
                .Build();
        }

        var roles = await _service.GetRolesAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._ModalClose)
            .AlsoUpdate(SwapElements.RoleList, SwapViews.TenantAdmin._RoleList, roles)
            .WithSuccessToast("Role updated")
            .Build();
    }

    [HttpPost("delete-role")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesDelete)]
    public async Task<IActionResult> DeleteRole([FromForm] string id)
    {
        var success = await _service.DeleteRoleAsync(id);
        var roles = await _service.GetRolesAsync();

        if (!success)
        {
            return SwapResponse()
                .WithErrorToast("Cannot delete system roles or roles with assigned users")
                .WithView(SwapViews.TenantAdmin._RoleList, roles)
                .Build();
        }

        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._RoleList, roles)
            .WithSuccessToast("Role deleted")
            .Build();
    }

    [HttpPost("toggle-role-permission")]
    [RequireFeature("custom_roles")]
    [HasPermission(TenantAdminPermissions.RolesEdit)]
    public async Task<IActionResult> ToggleRolePermission([FromForm] string roleId, [FromForm] Guid permissionId)
    {
        await _service.ToggleRolePermissionAsync(roleId, permissionId);

        var roles = await _service.GetRolesAsync();
        var role = roles.FirstOrDefault(r => r.Id == roleId);
        if (role is null) return NotFound();

        var permissions = await _service.GetPermissionsAsync();
        return SwapView(SwapViews.TenantAdmin._RoleDetail, new RoleDetailViewModel { Role = role, AllPermissions = permissions });
    }

    [HttpGet("manage-user-roles")]
    [HasPermission(TenantAdminPermissions.UsersEdit)]
    public async Task<IActionResult> ManageUserRoles(string id)
    {
        var roles = await _service.GetRolesAsync();
        var assignedRoleIds = await _service.GetUserRoleIdsAsync(id);
        var users = await _service.GetUsersAsync();
        var user = users.Items.FirstOrDefault(u => u.Id == id);
        if (user is null) return NotFound();

        return SwapView(SwapViews.TenantAdmin._ManageUserRolesModal, new ManageUserRolesViewModel
        {
            UserId = id,
            UserEmail = user.Email,
            AllRoles = roles,
            AssignedRoleIds = assignedRoleIds
        });
    }

    [HttpPost("save-user-roles")]
    [HasPermission(TenantAdminPermissions.UsersEdit)]
    public async Task<IActionResult> SaveUserRoles([FromForm] string userId, [FromForm] List<string> roleIds)
    {
        var success = await _service.SetUserRolesAsync(userId, roleIds ?? []);
        if (!success) return NotFound();

        var users = await _service.GetUsersAsync();
        return SwapResponse()
            .WithView(SwapViews.TenantAdmin._ModalClose)
            .AlsoUpdate(SwapElements.UserList, SwapViews.TenantAdmin._UserList, users)
            .WithSuccessToast("Roles updated")
            .Build();
    }

    [HttpPost("assign-role")]
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
            .WithView(SwapViews.TenantAdmin._UserList, users)
            .WithSuccessToast("Role assigned")
            .Build();
    }

    [HttpPost("remove-role")]
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
            .WithView(SwapViews.TenantAdmin._UserList, users)
            .WithSuccessToast("Role removed")
            .Build();
    }
}
