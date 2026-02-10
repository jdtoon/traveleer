# Shared

Interfaces, option classes, and contract records that modules depend on. This is the dependency boundary — modules reference `Shared/`, never each other.

## Interfaces

| Interface | Purpose | Implementations |
|-----------|---------|----------------|
| `IModule` | Module contract for registration, features, permissions, seeding | Each module |
| `IStorageService` | Blob storage (upload/download/delete) | `LocalStorageService`, `R2StorageService` |
| `IEmailService` | Email sending | `ConsoleEmailService`, `SesEmailService` |
| `IBillingService` | Payment/subscription management | `MockBillingService`, `PaystackBillingService` |
| `IBotProtection` | Bot/spam protection | `MockBotProtection`, `TurnstileBotProtection` |
| `IFeatureService` | Feature flag checks | `FeatureService` |
| `IAuditWriter` | Audit log writing | `ChannelAuditWriter` |
| `ICurrentUser` | Current authenticated user context | `CurrentUser` |
| `ITenantContext` | Current tenant context | `TenantContext` |

## Contract Records (`ModuleContracts.cs`)

| Record | Purpose |
|--------|---------|
| `ModuleFeature` | Feature declaration with `MinPlanSlug` for plan tier assignment |
| `ModulePermission` | Permission declaration for tenant provisioning |
| `RoleDefinition` | Role declaration for tenant provisioning |
| `RolePermissionMapping` | Maps permissions to roles for tenant provisioning |

## Option Classes

| Class | Config Section | Purpose |
|-------|---------------|---------|
| `SiteSettings` | `Site` | Base URL, name, support email |
| `EmailOptions` | `Email` | Provider, from address, SES config |
| `StorageOptions` | `Storage` | Provider, local path, R2 config |
| `BackupOptions` | `Backup` | Litestream config paths |
| `TurnstileOptions` | `Turnstile` | Provider, site/secret keys |
| `DevSeedOptions` | `DevSeed` | Dev seeding: enabled, tenant slug, emails, plan |
