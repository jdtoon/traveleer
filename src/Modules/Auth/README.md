# Auth Module

Magic link authentication, ASP.NET Identity, and authorization.

## Structure

```
Auth/
├── AuthModule.cs              — IModule implementation (SSO feature, public routes)
├── Entities/
│   ├── AppUser.cs             — Identity user (tenant DB)
│   ├── AppRole.cs             — Identity role (tenant DB)
│   ├── Permission.cs          — Permission entity (tenant DB)
│   ├── RolePermission.cs      — Role-permission mapping (tenant DB)
│   └── MagicLinkToken.cs      — Magic link tokens (core DB)
├── Data/
│   └── AuthConfigurations.cs  — EF configs (ITenantEntityConfiguration + ICoreEntityConfiguration)
├── Services/
│   ├── MagicLinkService.cs    — Token generation, email sending, validation
│   ├── MagicLinkCleanupService.cs — Background cleanup of expired tokens
│   ├── CurrentUser.cs         — ICurrentUser implementation
│   └── HasPermissionFilter.cs — MVC filter for permission checks
└── Views/Auth/                — Login page, magic link sent confirmation
```

## Auth Schemes

| Scheme | Cookie | Purpose |
|--------|--------|---------|
| `Tenant` | `.Tenant.Auth` | Tenant user authentication (12h expiry) |
| `SuperAdmin` | `.SuperAdmin.Auth` | Super admin authentication (24h expiry) |

## Magic Link Flow

1. User enters email on login page
2. `MagicLinkService` generates token, stores in core DB, sends email
3. User clicks link → token validated → cookie issued
4. Background service cleans up expired tokens

## Feature: `sso` (MinPlanSlug: `enterprise`)

Single Sign-On is available on Enterprise plan only.
