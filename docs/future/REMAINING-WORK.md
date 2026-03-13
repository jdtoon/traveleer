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

- no Phase 3 rate-card gap is currently verified after template, import-export, and non-hotel pricing support
- only small residual issues discovered during the final parity sweep should remain here

### Already Done

- rate-card CRUD
- reusable hotel rate-card templates
- non-hotel rate-category pricing for flights, excursions, transfers, and visas
- save current rate-card season structures as reusable templates
- single-card JSON export
- single-card CSV export
- export-all JSON bundle from the rate-card index
- JSON import preview with duplicate handling
- JSON import execution that backfills destinations, hotels, meal plans, room types, and rate-category mappings where needed
- CSV preview and re-import into an existing rate card
- season editing
- details and summary flow
- activation workflow

## Marketing

### Still Open

- no large marketing parity slice is currently open after the public-site refresh
- only small residual messaging or layout issues discovered during the final parity sweep should remain here

### Already Done

- travel-agency landing-page positioning and CTA refresh
- pricing-page travel workflow messaging and plan presentation refresh
- about, contact, terms, and privacy parity refresh
- responsive public marketing shell and contact flow QA

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
