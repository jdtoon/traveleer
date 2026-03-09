# Traveleer Migration Roadmap

## Goal

Evolve `traveleer` from SaaS starter to the full travel-agency product by porting the AgencyCardPro feature surface into Traveleer's multi-tenant architecture under ClinicDiary-style engineering discipline.

## Outcome Targets

- Traveleer keeps its SaaS core intact.
- AgencyCardPro tenant, public, and onboarding features are represented in Traveleer.
- Migrated UI is rebuilt in DaisyUI + Tailwind.
- Every migrated feature ships with service tests, HTMX integration tests, and manual Playwright QA.

## Phases

### Phase 1: Governance And Planning Foundation

Objectives:

- add and adapt instruction files
- create future-planning structure
- inventory AgencyCardPro features
- define branding, onboarding, data, and QA strategy

Outputs:

- Traveleer instruction files
- `docs/future` scaffold
- migration inventory and strategy documents

### Phase 2: Product Architecture Decisions

Objectives:

- finalize module boundaries for travel-domain features
- define target entities and storage ownership
- define branding architecture and onboarding contract
- decide how public marketing content evolves

Likely deliverables:

- module boundary specs
- entity/data ownership maps
- onboarding flow spec
- branding settings spec
- first-wave module specs for Clients and Inventory
- execution backlog mapping source modules to target work

### Phase 3: Core Module Migration

Objectives:

- migrate foundational tenant modules first
- establish travel-domain seed/demo data
- validate Traveleer shell navigation and layout patterns

Candidate first-wave modules:

- Clients
- Inventory
- Settings
- Branding
- Onboarding

### Phase 4: High-Complexity Workflow Migration

Objectives:

- migrate pricing, rate cards, quotes, bookings, vouchers, and document workflows
- redesign advanced screens for DaisyUI + HTMX
- verify calculations and workflow states under multi-tenant conditions

High-risk workstreams:

- RateCards
- Quotes
- Bookings
- document generation
- supplier request flows

### Phase 5: Public Site, Polish, And Release Hardening

Objectives:

- complete marketing-site rewrite for travel positioning
- align registration and onboarding experience
- run regression and UX sweeps across tenant and public surfaces
- close remaining parity gaps

## Workstream View

| Workstream | Runs Early | Runs Throughout |
|-----------|------------|-----------------|
| Instructions and governance | Yes | Light maintenance |
| Architecture decisions | Yes | Yes |
| UX and design system | Yes | Yes |
| Data/schema mapping | Yes | Yes |
| Module migration | After phase 1 | Yes |
| QA and regression | After first module | Yes |

## Dependency Rules

- No complex module migration without a corresponding future spec.
- Branding decisions come before PDF/email-heavy modules.
- Onboarding decisions come before full registration-to-first-use polish.
- Seed/demo data must exist before serious Playwright regression passes.

## Success Criteria

- Traveleer remains structurally coherent as a SaaS product.
- Migrated workflows feel native to Traveleer, not pasted from AgencyCardPro.
- Quality gates are consistently followed.
- The docs are good enough to drive module-by-module implementation without guesswork.
