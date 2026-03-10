# Remaining Migration Work

This folder is now the live execution tracker for the remaining AgencyCardPro parity work in `traveleer`.

It no longer describes speculative module migration. The large module migration is already done.

## What This Folder Tracks

- what is already complete in Traveleer
- the verified implementation gaps that still remain
- the ordered execution phases for closing those gaps
- the rule that each phase must end with build, tests, browser QA, a commit, and a docs update before the next phase starts

## Reading Order

1. `STATUS.md`
2. `REMAINING-WORK.md`
3. `PHASES.md`

## Operating Rule

For every remaining phase:

1. implement the phase
2. run `dotnet build src/saas.csproj`
3. run `dotnet test tests/saas.UnitTests`
4. run `dotnet test tests/saas.IntegrationTests`
5. perform manual browser QA
6. commit the phase
7. update these docs to mark it complete

## Current Position

Traveleer already contains the full ACP product-module surface:

- Branding
- Onboarding
- Clients
- Settings
- Inventory
- RateCards
- Quotes
- Email
- Bookings

The remaining work is feature-depth parity and public-site parity, not module creation.
