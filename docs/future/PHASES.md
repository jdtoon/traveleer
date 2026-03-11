# Phases

This is the ordered execution plan for the remaining migration work.

## Phase 0 - Docs Reset

Status: Complete

Scope:

- delete the stale speculative roadmap files
- replace them with current-state status and execution docs
- mark broad module migration as complete
- identify the real remaining parity gaps

Exit criteria:

- new `docs/future` files exist
- old speculative docs are gone
- repo is committed before Phase 1 begins

## Phase 1 - Quote Version History

Status: Complete

Scope:

- implement ACP-style quote version history or snapshots in Traveleer
- expose the history in the quote details experience
- keep the slice focused on versioning, not template work

Exit criteria:

- unit tests added or updated
- integration tests added or updated
- build and both test suites pass
- browser QA completed
- commit created
- this file updated before Phase 2 begins

## Phase 2 - Quote Depth Parity

Status: Complete

Scope:

- persist ACP-style quote display settings in Traveleer quotes
- expose template-layout selection and preview display toggles in the quote builder
- make the saved quote preview reflect those settings reliably

Exit criteria:

- unit and integration coverage updated
- build and both test suites pass
- browser QA completed
- commit created
- docs updated

## Phase 3 - RateCards Depth Parity

Status: Next

Scope:

- implement ACP rate-card templates
- implement ACP import and export support
- close any clearly verified non-hotel pricing gaps

Exit criteria:

- unit and integration coverage updated
- build and both test suites pass
- browser QA completed
- commit created
- docs updated

## Phase 4 - Marketing Parity

Status: Pending

Scope:

- compare ACP business-site content against Traveleer marketing views
- port remaining meaningful copy and structure into Traveleer
- review terms and privacy alignment

Exit criteria:

- changed pages covered appropriately
- build and both test suites pass
- browser QA completed
- commit created
- docs updated

## Phase 5 - Final Parity Sweep

Status: Pending

Scope:

- recheck Quotes, RateCards, Marketing, Email, Bookings, and Onboarding for residual parity gaps
- fix small remaining gaps
- leave the docs in a final accurate state

Exit criteria:

- full quality gate passes
- browser QA completed for changed flows
- final commit created
- remaining docs reflect the final state
