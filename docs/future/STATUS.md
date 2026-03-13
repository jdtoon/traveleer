# Status

## Completed Foundation

The following roadmap areas are complete and no longer belong in a speculative future plan:

- instruction adoption and quality-gate rollout
- tenant-scoped Branding module
- tenant-scoped Onboarding module
- Clients module
- Settings module
- Inventory module
- RateCards core module
- Quotes core module
- Email module
- Bookings core module
- supplier request and reminder workflow
- voucher generation and voucher PDF flow
- supplier voucher sending

## Completed Implementation Reality

The Traveleer app modules registered in `src/Program.cs` already include:

- `BrandingModule`
- `BookingsModule`
- `ClientsModule`
- `EmailModule`
- `InventoryModule`
- `OnboardingModule`
- `QuotesModule`
- `RateCardsModule`
- `SettingsModule`

Automated coverage also exists across the main migrated travel modules under:

- `tests/saas.UnitTests/Modules/*`
- `tests/saas.IntegrationTests/*`

## What Is Still Open

No verified ACP implementation gaps are currently open in the migrated Traveleer modules.

Any future work should now be treated as new product evolution, not migration-parity cleanup, unless a fresh verified gap is discovered.

## Current Phase

- `Phase 0` complete: docs reset from speculative roadmap to live execution tracker
- `Phase 1` complete: quote version history and snapshot details
- `Phase 2` complete: quote display settings, template layouts, and live preview parity
- `Phase 3` complete: reusable rate-card templates plus JSON/CSV import-export workflows are live, including non-hotel rate-category pricing support
- `Phase 4` complete: marketing pages now reflect ACP-aligned travel-agency positioning, pricing, contact, and legal-page parity
- `Phase 5` complete: final residual parity sweep across quotes, bookings, email, onboarding, and remaining UX polish
