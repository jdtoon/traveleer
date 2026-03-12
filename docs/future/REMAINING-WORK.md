# Remaining Work

This file lists only verified ACP implementation gaps that are still worth closing.

## Quotes

### Still Open

- any residual quote autosave or export depth that still proves justified during the final parity sweep

### Already Done

- quote builder create and edit flow
- quote version history and snapshot details
- quote display settings and template layout selection
- live preview layout switching and display toggles
- quote preview flow
- quote details flow
- quote status updates
- quote email compose, send, and history
- quote-to-booking conversion support in downstream flow

## RateCards

### Still Open

- any clearly missing non-hotel pricing support that ACP already implements and Traveleer still lacks

### Already Done

- rate-card CRUD
- reusable hotel rate-card templates
- save current rate-card season structures as reusable templates
- single-card JSON export
- single-card CSV export
- export-all JSON bundle from the rate-card index
- JSON import preview with duplicate handling
- JSON import execution that backfills destinations, hotels, meal plans, and room types where needed
- CSV preview and re-import into an existing hotel rate card
- season editing
- details and summary flow
- activation workflow

## Marketing

### Still Open

- ACP business-site content and structure parity review
- terms and privacy alignment review
- any remaining travel-specific positioning gaps in Traveleer marketing pages

### Already Done

- Traveleer already has a functioning marketing module with landing, pricing, about, contact, terms, privacy, and login redirect pages

## Bookings

### Still Open

- no large booking-module parity slice is currently open after supplier voucher sending
- only small residual issues discovered during the final parity sweep should remain here

### Already Done

- booking create and details flow
- booking items and supplier status workflow
- supplier request and reminder flow
- supplier confirmation and decline flow
- voucher generation
- voucher preview and PDF retrieval
- supplier voucher sending with email attachment

## Onboarding

### Still Open

- review whether ACP onboarding still has meaningful guided setup depth worth porting after the higher-priority quote and rate-card work is finished

### Already Done

- tenant onboarding shell
- identity step
- defaults step
- completion step
