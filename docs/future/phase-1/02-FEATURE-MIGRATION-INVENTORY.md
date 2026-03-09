# Phase 1 - Feature Migration Inventory

## Goal

Map AgencyCardPro's product surface into Traveleer's target architecture.

## Source Systems

- Traveleer: retained SaaS platform and destination codebase
- AgencyCardPro: source of travel-domain behavior and public/product content
- ClinicDiary: source of implementation discipline and planning model

## Inventory Buckets

### A. Retain As-Is In Traveleer Foundation

These already exist in Traveleer and should not be replaced by AgencyCardPro equivalents:

- Tenancy
- Auth
- Registration
- Billing
- SuperAdmin
- FeatureFlags
- Dashboard
- TenantAdmin
- Audit
- Notifications
- Marketing
- Litestream

### B. Public And Early-Lifecycle Surfaces To Rework

| Area | Source | Destination Direction |
|------|--------|-----------------------|
| Marketing site | `agencycardpro/business-site/*` | Rewrite inside Traveleer `Marketing` module |
| Terms and privacy content | AgencyCardPro business site | Move into Traveleer marketing views |
| Onboarding wizard | `agencycardpro/src/Modules/Onboarding/` | Redesign as post-registration tenant onboarding |
| Branding setup | `agencycardpro/src/Modules/Branding/` | Redesign as tenant-scoped branding/settings |

### C. Core Tenant Modules To Port

| AgencyCardPro Module | Target Direction In Traveleer | Notes |
|----------------------|-------------------------------|-------|
| Clients | New app module | Early-wave, lower complexity |
| Inventory | New app module | Early-wave, foundational for pricing and quotes |
| Settings | New app module or TenantAdmin extension | Needs boundary decision |
| Branding | New app module or settings extension | Must be tenant-scoped |
| RateCards | New app module | High-risk pricing/import work |
| Quotes | New app module | High-risk builder and document flow |
| Bookings | New app module | High-risk workflow and supplier actions |

### D. Shared Logic Worth Reusing

Potential reusable patterns from AgencyCardPro:

- numbering services
- pricing and calculation services
- pagination/filter DTO patterns
- PDF generation abstraction
- import/export service patterns
- email workflow orchestration patterns

These should be rewritten into Traveleer module/service boundaries instead of copied wholesale.

### E. Areas That Must Be Redesigned For Multi-Tenancy

- branding settings and dynamic theme output
- onboarding progress and defaults
- document generation using tenant identity
- any single-agency assumptions in settings or numbering
- any logic that currently assumes one deployment equals one agency

## Suggested Migration Order

1. Branding and onboarding architecture
2. Clients
3. Inventory
4. Settings
5. RateCards
6. Quotes
7. Bookings
8. Public marketing polish and parity sweep

## Risk Flags

### High Risk

- RateCards
- Quotes
- Bookings
- branding architecture
- onboarding redesign

### Medium Risk

- public marketing rewrite
- settings ownership split
- PDF and email identity customization

### Lower Risk

- Clients
- Inventory basic CRUD

## Required Spec Before Implementation

The following cannot start as direct code work without their own deeper spec:

- Branding
- Onboarding
- RateCards
- Quotes
- Bookings
