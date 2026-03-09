# Phase 2 - Branding Module Spec

## Goal

Introduce tenant-scoped branding to Traveleer without undermining the shared DaisyUI + Tailwind design system.

## Source Reference

AgencyCardPro branding concepts live in:

- `c:/dev/agencycardpro/src/Modules/Branding/Controllers/BrandingController.cs`
- `c:/dev/agencycardpro/src/Modules/Branding/Entities/AgencySettings.cs`
- `c:/dev/agencycardpro/src/Modules/Branding/Infrastructure/AgencyBrandingFilter.cs`

## Target Direction

Branding in Traveleer should become a tenant-owned app feature. It may live either:

- as a dedicated `Branding` module under `src/Modules/Branding/`, or
- as a branding slice inside `TenantAdmin` if we later decide not to split it out

Current recommendation: use a dedicated `Branding` module and expose management UI through tenant-admin navigation.

## Why Direct Porting Is Wrong

AgencyCardPro assumes one agency per deployment and stores one global settings record. Traveleer provisions many tenants and must isolate brand state per tenant.

## Target Responsibilities

The Traveleer branding feature should own:

- tenant display name and presentation settings that go beyond core tenant identity
- tenant logo references
- brand color settings with validation and fallback defaults
- a tenant-specific CSS variable endpoint
- document and email brand settings where needed
- preview and validation UX for branding edits

## Data Ownership

Branding data should be tenant-owned.

Recommended entity shape:

- `TenantBrandingSettings`
- one record per tenant
- stored in `TenantDbContext`
- linked conceptually to the current tenant context, not to platform-wide public branding

Suggested fields:

- `Id`
- `DisplayName`
- `LogoStorageKey` or `LogoUrl`
- `PrimaryColor`
- `SecondaryColor`
- `AccentColor`
- `DocumentHeaderText`
- `DocumentFooterText`
- `EmailHeaderText`
- `EmailFooterText`
- audit fields if applicable

## HTTP Surface

### Tenant Settings UI

Recommended tenant routes:

- `GET /{slug}/branding`
- `GET /{slug}/branding/edit`
- `POST /{slug}/branding/update`
- `GET /{slug}/branding/preview`

### Theme Output

Recommended public-within-tenant route:

- `GET /{slug}/branding/theme.css`

Notes:

- the route is tenant-qualified
- it should emit CSS custom properties only
- it should not dump raw user input without validation
- it should not disable the existing DaisyUI approach

## Controller Pattern

- inherit `SwapController`
- use `SwapView()` for full pages
- use `PartialView()` for edit forms and previews
- use `SwapResponse()` for successful saves and error states
- permissions should align with tenant-admin settings permissions or dedicated branding permissions

## Service Layer

Recommended service contract:

- `GetAsync()`
- `GetEditModelAsync()`
- `UpdateAsync(BrandingSettingsDto dto)`
- `BuildThemeCssAsync()`
- `ValidateColorsAsync()` or equivalent validation logic

Service logic should handle:

- default fallback values
- normalization of hex colors
- sanitization of presentation fields
- mapping to document and email rendering contracts

## UX Contract

The branding page should include:

- current tenant brand summary
- live or near-live preview area
- clear explanation of what changes affect app chrome versus emails/documents
- safe defaults and reset-to-default action
- accessible validation when colors break contrast rules or input format

## Integration Points

- `src/Views/Shared/_TenantLayout.cshtml`
- email rendering services
- future quote and booking PDF services
- tenant admin navigation and settings information architecture

## Tests Required

### Unit Tests

- color normalization
- fallback default assignment
- invalid color rejection
- CSS output generation

### Integration Tests

- branding page full render
- edit modal or form partial render
- valid update flow emits success feedback and refreshes preview
- invalid update flow returns inline errors
- `/{slug}/branding/theme.css` returns tenant-specific CSS variables

### Manual QA

- preview is understandable
- updated branding appears in tenant shell where intended
- default tenant without customization still renders cleanly
- no unreadable contrast combinations slip through

## Open Decisions

- should display name duplicate or extend core tenant name?
- should logos be stored through Traveleer's storage service from day one?
- what exact subset of emails/documents is branding-aware in phase 1 versus later phases?
