# Traveleer Future Work

This folder is the planning and design space for turning `traveleer` into the full travel-agency product built on the SaaS starter and informed by `agencycardpro`.

## Intent

Traveleer keeps the existing multi-tenant SaaS foundation and absorbs product behavior from AgencyCardPro through deliberate migration, redesign, and testing.

This folder exists to make implementation predictable:

- architecture decisions are written down before code starts
- high-risk migrations are specified before they are implemented
- UX is designed before screens are built
- testing and manual QA are part of the plan, not cleanup work

## Reading Order

1. `DECISIONS.md`
2. `ROADMAP.md`
3. `phase-1/01-INSTRUCTION-ADOPTION.md`
4. `phase-1/02-FEATURE-MIGRATION-INVENTORY.md`
5. `phase-1/03-BRANDING-THEMING.md`
6. `phase-1/04-DATA-MIGRATION-STRATEGY.md`
7. `phase-1/05-TESTING-QA-ROLLOUT.md`
8. `phase-1/06-PUBLIC-SITE-AND-ONBOARDING.md`

## Current Foundation

| Area | Current State | Direction |
|------|---------------|-----------|
| SaaS core | Present in `traveleer` | Retain and extend |
| Tenant registration | Present in `Registration` module | Keep, then layer onboarding |
| Billing | Present in `Billing` module | Retain unless ADR says otherwise |
| Marketing site | Present in `Marketing` module | Redesign content for travel product |
| Tenant product modules | Not yet implemented | Port from AgencyCardPro into app modules |
| Engineering instructions | Missing in Traveleer | Import and adapt from ClinicDiary |
| Future planning docs | Missing in Traveleer | Establish here |

## Phase Structure

- `phase-1/` - governance, migration inventory, branding, data mapping, testing rollout, onboarding/public-site alignment
- Later phases - module specs, workflow redesigns, implementation sequencing, and release planning

## Working Rules

- No major module migration starts without a corresponding future doc when the flow is complex.
- Quotes, bookings, rate cards, branding, and onboarding always require written design before coding.
- The destination UI system is DaisyUI + Tailwind, not AgencyCardPro's custom CSS.
- The quality gate is build + unit tests + integration tests + Playwright QA.
