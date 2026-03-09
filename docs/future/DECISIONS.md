# Traveleer Decisions

This file records accepted architectural and migration decisions for the Traveleer product build-out.

## ADR-001: Retain Traveleer As The Product Base

- Status: Accepted
- Date: 2026-03-09

### Context

We have three related codebases:

- `traveleer` - the SaaS starter foundation
- `agencycardpro` - the existing travel product with domain behavior
- `clinicdiary` - the mature example of stricter engineering, documentation, and QA discipline

The migration could either copy AgencyCardPro wholesale into a new shell or retain Traveleer and port product behavior into it.

### Decision

Retain Traveleer as the base application and migrate AgencyCardPro features into it.

### Rationale

- Traveleer already has the multi-tenant architecture we want to preserve.
- Registration, billing, tenant resolution, marketing, and platform modules already exist.
- AgencyCardPro contains domain logic worth porting, but not the destination architecture.
- Keeping Traveleer as the base minimizes architectural regression and avoids reintroducing single-tenant assumptions.

### Alternatives Considered

- Rebuild Traveleer from AgencyCardPro as the base.
  Rejected because AgencyCardPro is single-tenant and would require re-deriving SaaS platform concerns.
- Merge code opportunistically without a retained base decision.
  Rejected because it would blur ownership boundaries and raise migration risk.

## ADR-002: Adopt ClinicDiary Quality Gates In Traveleer

- Status: Accepted
- Date: 2026-03-09

### Context

Traveleer currently lacks the instruction and testing discipline that exists in ClinicDiary.

### Decision

Adopt ClinicDiary-style development and testing gates in Traveleer, adapted to Traveleer's actual module contract and project names.

### Rationale

- The migration is large enough that quality must be systematized.
- UX, testing, and manual QA need to be part of the workflow from the start.
- A shared operating model reduces drift as modules are ported from AgencyCardPro.

### Implements

- `.github/instructions/development.instructions.md`
- `.github/instructions/testing.instructions.md`

## ADR-003: Port Logic, Rebuild UI

- Status: Accepted
- Date: 2026-03-09

### Context

AgencyCardPro's product surface is useful, but its UI stack relies on custom CSS and patterns that do not fit Traveleer's DaisyUI + Tailwind + HTMX approach.

### Decision

Default migration mode is to port business logic and workflows while rebuilding views and interaction patterns in Traveleer's UI system.

### Rationale

- It keeps the final product visually and architecturally coherent.
- It avoids carrying forward inline-style-heavy or one-off UI patterns.
- It aligns the product with the same testing and UX discipline expected in Traveleer.

### Exceptions

A raw carry-over is allowed only for content or assets where the destination stack is not compromised.

## ADR-004: Branding Is First-Roadmap Scope

- Status: Accepted
- Date: 2026-03-09

### Context

AgencyCardPro has a branding concept, but it is built for one agency per deployment. Traveleer needs a multi-tenant answer.

### Decision

Branding will be designed as first-roadmap scope and treated as a multi-tenant feature, not a late enhancement.

### Rationale

- Branding affects layouts, tenant settings, emails, PDFs, and onboarding.
- Delaying it would force rework into multiple migrated modules.
- The product direction explicitly includes it.

## ADR-005: Public Marketing Brand And Tenant Brand Are Separate Concerns

- Status: Accepted
- Date: 2026-03-09

### Context

The SaaS platform has its own public marketing site, while each tenant may later have its own in-app brand treatment.

### Decision

Keep platform marketing branding and tenant in-app branding separate unless a future ADR explicitly introduces white-label public marketing.

### Rationale

- This preserves a clear platform identity.
- It simplifies early architecture for registration and marketing pages.
- It still leaves room for per-tenant branding in app shells, emails, and PDFs.
