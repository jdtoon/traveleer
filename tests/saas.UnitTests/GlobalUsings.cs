// Entity types moved from Data/{Core,Tenant,Audit} into module folders.
// These global usings ensure test files can resolve entity types without
// updating every individual test file's using statements.

global using saas.Modules.Billing.Entities;
global using saas.Modules.FeatureFlags.Entities;
global using saas.Modules.Auth.Entities;
global using saas.Modules.Tenancy.Entities;
global using saas.Modules.SuperAdmin.Entities;
global using saas.Modules.Audit.Entities;
global using saas.Modules.Notes.Entities;
