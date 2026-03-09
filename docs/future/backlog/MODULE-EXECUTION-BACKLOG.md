# Module Execution Backlog

This backlog maps AgencyCardPro source areas to Traveleer target workstreams.

## Foundation And Cross-Cutting

| Work Item | Source | Target | Status | Risk |
|-----------|--------|--------|--------|------|
| Instruction adoption | `clinicdiary/.github/instructions/*` | `traveleer/.github/instructions/*` | Completed | Low |
| Future-planning scaffold | `clinicdiary/docs/future/*` concept | `traveleer/docs/future/*` | In progress | Low |
| Branding architecture | `agencycardpro/src/Modules/Branding/*` | Traveleer branding spec and later module | Spec written | High |
| Onboarding architecture | `agencycardpro/src/Modules/Onboarding/*` | Traveleer onboarding spec and later module | Spec written | High |

## Public And Entry Flows

| Work Item | Source | Target | Status | Risk |
|-----------|--------|--------|--------|------|
| Marketing content migration | `agencycardpro/business-site/*` | `traveleer/src/Modules/Marketing/*` | Planned | Medium |
| Terms and privacy content alignment | `agencycardpro/business-site/privacy.html`, `agencycardpro/business-site/terms.html` | Traveleer marketing views | Planned | Low |
| Registration-to-onboarding flow | Traveleer `Registration` + AgencyCardPro onboarding concept | Traveleer registration and onboarding integration | Planned | Medium |

## First-Wave Tenant Modules

| Work Item | Source | Target | Status | Risk |
|-----------|--------|--------|--------|------|
| Clients module | `agencycardpro/src/Modules/Clients/*` | `traveleer/src/Modules/Clients/*` | Spec written | Medium |
| Inventory module | `agencycardpro/src/Modules/Inventory/*` | `traveleer/src/Modules/Inventory/*` | Spec written | Medium |
| Tenant branding management | ACP Branding concept | `traveleer/src/Modules/Branding/*` or TenantAdmin extension | Spec written | High |
| Tenant onboarding flow | ACP Onboarding concept | `traveleer/src/Modules/Onboarding/*` | Spec written | High |

## Later High-Complexity Modules

| Work Item | Source | Target | Status | Risk |
|-----------|--------|--------|--------|------|
| RateCards | `agencycardpro/src/Modules/RateCards/*` | `traveleer/src/Modules/RateCards/*` | Not started | High |
| Quotes | `agencycardpro/src/Modules/Quotes/*` | `traveleer/src/Modules/Quotes/*` | Not started | High |
| Bookings | `agencycardpro/src/Modules/Bookings/*` | `traveleer/src/Modules/Bookings/*` | Not started | High |
| Document generation and vouchers | ACP quote/booking PDF services | Traveleer document services and modules | Not started | High |

## Suggested Implementation Queue

1. Branding module decision and settings ownership finalization
2. Onboarding module decision and redirect contract
3. Clients module implementation
4. Inventory module implementation
5. Marketing content migration pass
6. RateCards spec and implementation
7. Quotes spec and implementation
8. Bookings spec and implementation

## Required Rule For Every Backlog Item

Before code starts, each item must have:

- source path reference
- target path reference
- data ownership notes
- UX redesign notes
- unit/integration/manual QA expectations
