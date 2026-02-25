# TenantAdmin Module

Tenant-scoped administration — user management, role/permission management, team invitations, billing UI, tenant settings, and tenant lifecycle (deletion/suspension). Accessible to tenant users with appropriate permissions.

## Structure

```
TenantAdmin/
├── TenantAdminModule.cs                 # Permissions, features, controller view paths
├── Entities/
│   └── TeamInvitation.cs                # Tenant DB: email invitations
├── Models/
│   ├── InviteUserRequest.cs
│   └── TenantSettingsModel.cs
├── Data/
│   └── TeamInvitationTenantConfiguration.cs
├── Services/
│   ├── TenantAdminService.cs            # User/role/permission CRUD
│   └── TenantLifecycleService.cs        # Tenant deletion, data export
├── Controllers/
│   ├── TenantAdminController.cs         # User and role management
│   ├── TenantBillingController.cs       # Billing dashboard (wraps IBillingService)
│   ├── TenantSettingsController.cs      # Tenant settings page
│   └── InvitationController.cs          # Send/accept team invitations
└── Views/
    ├── TenantAdmin/                     # Users list, roles list, modals
    ├── TenantBilling/                   # Billing dashboard, plan change, invoices
    ├── TenantSettings/                  # Settings page
    └── Invitation/                      # Invite flow, accept page
```

## Permissions

This module declares 10 permissions across 3 groups:

| Permission Key | Display Name | Group |
|---------------|-------------|-------|
| `users.read` | View Users | Users |
| `users.create` | Invite Users | Users |
| `users.edit` | Edit Users | Users |
| `users.delete` | Deactivate Users | Users |
| `roles.read` | View Roles | Roles |
| `roles.create` | Create Roles | Roles |
| `roles.edit` | Edit Roles | Roles |
| `roles.delete` | Delete Roles | Roles |
| `settings.read` | View Settings | Settings |
| `settings.edit` | Edit Settings | Settings |

**Default role mapping:**
- **Admin** — gets all permissions automatically (system-wide rule)
- **Member** — gets `users.read`, `roles.read`, `settings.read` (read-only access)

## Feature Flags

| Feature Key | Display Name | Min Plan |
|------------|-------------|----------|
| `custom_roles` | Custom Roles | `starter` |

When `custom_roles` is disabled (Free plan), tenants can only use the default Admin/Member roles and cannot create custom roles.

## Service APIs

### `ITenantAdminService`

| Method | Purpose |
|--------|---------|
| `GetUsersAsync(page, pageSize)` | Paginated user list |
| `InviteUserAsync(email, roleId?)` | Send team invitation email |
| `DeactivateUserAsync(userId)` | Deactivate a user (soft disable) |
| `ActivateUserAsync(userId)` | Reactivate a user |
| `GetRolesAsync()` | List all roles with permission counts |
| `GetPermissionsAsync()` | List all available permissions |
| `CreateRoleAsync(name, description)` | Create custom role (requires `custom_roles` feature) |
| `UpdateRoleAsync(roleId, name, description)` | Update role metadata |
| `DeleteRoleAsync(roleId)` | Delete custom role (system roles protected) |
| `ToggleRolePermissionAsync(roleId, permissionId)` | Toggle a permission on/off for a role |
| `AssignRoleAsync(userId, roleId)` | Assign a role to a user |
| `RemoveRoleAsync(userId, roleId)` | Remove a role from a user |
| `SetUserRolesAsync(userId, roleIds)` | Replace all roles for a user |
| `GetUserRoleIdsAsync(userId)` | Get user's current role IDs |

### `ITenantLifecycleService`

| Method | Purpose |
|--------|---------|
| `ExportTenantDataAsync()` | Export all tenant data as a ZIP archive |
| `RequestDeletionAsync(gracePeriodDays)` | Schedule tenant deletion (default 30-day grace) |
| `CancelDeletionAsync()` | Cancel a pending deletion |
| `PermanentlyDeleteTenantAsync(tenantId)` | Permanently delete tenant + database (called by `TenantDeletionJob`) |

## Routes

All routes use `/{slug}/` prefix and require `TenantUser` policy plus specific permissions.

### User Management (`TenantAdminController`)

| URL | Permission | Description |
|-----|-----------|-------------|
| `/{slug}/admin/users` | `users.read` | User list |
| `/{slug}/admin/users/invite` | `users.create` | Invite user modal |
| `/{slug}/admin/users/{id}/edit` | `users.edit` | Edit user modal |
| `/{slug}/admin/users/{id}/deactivate` | `users.delete` | Deactivate user |
| `/{slug}/admin/roles` | `roles.read` | Role list |
| `/{slug}/admin/roles/create` | `roles.create` | Create role modal |
| `/{slug}/admin/roles/{id}/edit` | `roles.edit` | Edit role + permissions |
| `/{slug}/admin/roles/{id}/delete` | `roles.delete` | Delete role |

### Billing (`TenantBillingController`)

| URL | Permission | Description |
|-----|-----------|-------------|
| `/{slug}/billing` | `settings.read` | Billing dashboard (plan, usage, invoices, payment methods) |
| `/{slug}/billing/change-plan` | `settings.edit` | Plan change modal |
| `/{slug}/billing/seats` | `settings.edit` | Seat/practice count change |
| `/{slug}/billing/addons` | `settings.read` | Add-on management |
| `/{slug}/billing/invoices/{id}` | `settings.read` | Invoice detail |
| `/{slug}/billing/callback` | — | Paystack payment callback |

### Settings (`TenantSettingsController`)

| URL | Permission | Description |
|-----|-----------|-------------|
| `/{slug}/settings` | `settings.read` | Tenant settings page |
| `/{slug}/settings/delete` | `settings.edit` | Request tenant deletion |

### Invitations (`InvitationController`)

| URL | Auth | Description |
|-----|------|-------------|
| `/{slug}/invite` | `users.create` | Send invitation |
| `/invite/accept?token=xxx` | Public | Accept invitation page |

## Team Invitation Flow

```
Admin invites user → POST /{slug}/invite (email, role)
    │
    ├─ Creates TeamInvitation (token, 7-day expiry)
    ├─ Sends invitation email with link
    │
    ▼
User clicks link → GET /invite/accept?token=xxx
    │
    ├─ Token valid + not expired?
    │     ├─ User exists? → Assigns role, marks accepted
    │     └─ New user? → Creates account, assigns role, marks accepted
    │
    └─ Expired/invalid → error page
```

## Billing Dashboard

The billing UI lives in this module but **delegates all operations to `IBillingService`**. It displays:

- Current plan name, price, and billing cycle
- Seat count with change controls
- Usage summary (if usage billing enabled)
- Estimated next invoice (base + variable charges)
- Add-ons management
- Recent invoices with detail views
- Payment methods
- Plan change modal (lists all available plans)
- Cancel subscription flow

No billing logic lives here — it's all in the Billing module. The views render data from the `BillingDashboard` record returned by `IBillingService.GetBillingDashboardAsync()`.
