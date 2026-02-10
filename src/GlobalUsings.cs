// =============================================================================
// Global Using Directives — Entity namespaces from module folders
// =============================================================================
// Entities were moved from Data/{Core,Tenant,Audit} into their owning modules.
// These global usings ensure all entity types (Plan, Tenant, Feature, AppUser,
// Permission, AuditEntry, Note, etc.) are available project-wide without
// explicit per-file imports.
// =============================================================================

global using saas.Modules.Billing.Entities;
global using saas.Modules.FeatureFlags.Entities;
global using saas.Modules.Auth.Entities;
global using saas.Modules.Tenancy.Entities;
global using saas.Modules.SuperAdmin.Entities;
global using saas.Modules.Audit.Entities;
global using saas.Modules.Notes.Entities;
