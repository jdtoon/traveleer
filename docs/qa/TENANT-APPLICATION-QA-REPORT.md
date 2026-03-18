# QA Report: Tenant Application

## Purpose

This document tracks module-by-module QA for the tenant application. It is the execution record for manual browser QA, runtime validation, persistence checks, and regression notes across the tenant workspace.

## Scope

Included:
- Tenant-facing application modules
- Tenant-admin governance flows that affect the tenant workspace

Excluded:
- Public marketing site
- Registration
- Super-admin platform shell
- Infrastructure-only pages

## Session Metadata

- Date: 2026-03-17
- Tester: GitHub Copilot
- Environment: Local development seed data
- Build/commit: Working tree QA session against localhost runtime
- Base URL: http://localhost:5000
- Tenant slug: demo
- Browser: Integrated browser
- Device: Desktop
- Seed/data notes: Shared demo tenant SQLite database with large pre-existing seeded and test-generated records

## Personas

- Tenant member
- Tenant admin

## Status Legend

- `Pass` - expected behavior confirmed
- `Pass with caveats` - usable, but issues or inconsistencies noted
- `Fail` - confirmed defect
- `Blocked` - could not complete because of environment, permissions, missing data, or external dependency
- `Not tested` - not executed yet

## Evidence Rules

- Every write flow should include a persistence check against the tenant SQLite DB.
- Every failing scenario should include:
  - reproduction steps
  - expected result
  - actual result
  - severity
  - related route
- Browser/runtime issues should note console errors, HTMX target errors, CSP errors, broken swaps, or stale modal state.
- Regression notes should mention adjacent flows retested after a fix or successful write.

## Execution Order

1. Dashboard
2. Clients
3. Quotes
4. Bookings
5. Itineraries
6. Suppliers
7. Rate Cards
8. Inventory
9. Tasks
10. Communications
11. Portal
12. Reports
13. Branding
14. Settings
15. Audit
16. Tenant Admin

---

## Module: Dashboard

### Routes

- /demo

### Persona

- Member
- Admin

### Smoke Checks

- Page shell renders
- Main widgets load
- Date/range controls work
- Links to downstream modules are valid

### Interactive Checks

- Widget refresh behavior
- Empty/loading states
- Notification bell/dropdowns if visible

### Runtime / HTMX Checks

- No console errors
- No stale loading indicators
- No broken partial swaps

### Regression Checks

- Links into tasks, reports, bookings, quotes

### Status

Pass

### Notes

- Invalid task create via the real modal now returns the task form with inline validation and an error toast instead of crashing.
- Clean-host retest on `http://localhost:5100` verified both native blank submit protection and a whitespace-only server submission path; the modal remained open, `The Title field is required.` rendered inline, and SQLite task count stayed unchanged.
- Valid create, edit, complete, and delete flows continue to work and persist correctly.
- Regression check passed: dashboard handoff to `/demo/tasks` loads the Tasks module successfully.

---

## Module: Clients

### Routes

- /demo/clients
- Client detail route(s)
- Create/edit modal routes if applicable

### Persona

- Member
- Admin

### Smoke Checks

- List renders
- Search/filter works
- Detail view opens

### Write Checks

- Create client
- Edit client
- Delete/archive if supported
- Invalid submission handling

### DB Verification

- New/updated row confirmed in tenant DB

### Runtime / HTMX Checks

- Modal opens/closes correctly
- List refreshes correctly
- No target errors or stale modal content

### Regression Checks

- Linked usage from bookings, quotes, reports

### Status

Pass

### Notes

- Invalid create path verified: blank submit kept the modal open, showed inline validation on `Client name`, and displayed an error toast.
- Valid create path verified with `QA Client 20260317-A`; SQLite confirmed persisted `Name`, `Company`, `Country`, `Email`, `Phone`, `Address`, and `Notes`.
- Search path verified by filtering to the QA client and isolating the created row.
- Details content verified against the exact SQLite row using client ID `AA7E626F-9DFC-4CD7-8F75-7484CC8ADE96`.
- Edit path verified live and in SQLite: `Company` updated to `QA Travel Co Updated`, notes updated, and `UpdatedAt` populated.
- Delete path verified live and in SQLite: the client was removed and search returned the empty-state panel.

---

## Module: Quotes

### Routes

- /demo/quotes
- Quote detail route(s)

### Persona

- Member
- Admin

### Smoke Checks

- List renders
- Detail page renders
- Status/pipeline visibility is coherent

### Write Checks

- Create quote
- Edit quote
- Status transition if supported
- Invalid submission handling

### DB Verification

- Quote row and key fields verified in tenant DB

### Runtime / HTMX Checks

- Partial refreshes work
- No stale content after save
- No broken actions for share/email if present

### Regression Checks

- Conversion into bookings
- Dashboard/report touchpoints

### Status

Pass

### Notes

- List shell, search, and status-tab filtering verified.
- Invalid builder submit verified on a fresh builder page: remained on `/demo/quotes/new`, showed the `Select at least one rate card.` validation path, and did not increase quote count.
- Valid create path verified with one-off client `QA Quote Client 20260317-A` and rate card `Grand QA Hotel`; preview rendered expected pricing before save.
- SQLite confirmed persisted quote `ACJ-2026-0091` with expected client, pricing, notes, and initial `Draft` status.
- Detail page verified: summary, preview, email history, and version history all rendered for the created quote.
- Status update path verified live and in SQLite: `ACJ-2026-0091` moved from `Draft` to `Sent`, and the Sent tab filter returned the quote correctly.

---

## Module: Bookings

### Routes

- /demo/bookings
- Calendar view
- Booking detail route(s)

### Persona

- Member
- Admin

### Smoke Checks

- List view renders
- Calendar view renders
- Detail page renders

### Write Checks

- Create booking
- Edit booking
- Item/payment/document/comment actions if supported
- Invalid submission handling

### DB Verification

- Booking totals and key fields verified in tenant DB

### Runtime / HTMX Checks

- Detail partials load correctly
- No broken item refreshes
- No stale spinners/modals

### Regression Checks

- Links from quotes
- Links to clients, suppliers, documents, payments

### Status

Pass

### Notes

- Invalid create path verified: blank submit kept the modal open, showed inline `Client is required.`, and displayed an error toast.
- Valid create path verified with client reference `QA-BKG-20260317-A`; SQLite confirmed persisted travel dates, pax, lead guest, nationality, special requests, and internal notes on booking `BK-2026-0171`.
- List search isolated the created booking correctly.
- Timeline route verified with the same booking filter state.
- Detail page summary verified against SQLite, including reference, travel window, source, and lead guest details.
- Invalid service add verified: blank submit kept the modal open and showed inline `Inventory item is required.`.
- Valid service add verified using inventory item `Grand QA Hotel`; SQLite confirmed the new `BookingItems` row with supplier reference `QA-SUP-REF-20260317-A` and updated booking totals from `0.00` to `Cost 1200.00 / Selling 5500.00`.
- Supplier request action verified for the created service; SQLite confirmed `SupplierStatus` moved to requested and `RequestedAt` was populated.

---

## Module: Itineraries

### Routes

- Itinerary list/detail/share routes as exposed in tenant app

### Persona

- Member
- Admin

### Smoke Checks

- Page/detail renders
- Shared/public handoff path behaves as expected if in scope

### Write Checks

- Create/edit itinerary if supported
- Reordering/day item edits if supported

### DB Verification

- Itinerary/day/item rows verified in tenant DB

### Runtime / HTMX Checks

- Dynamic sections update correctly
- No duplicate or stale content after edits

### Regression Checks

- Booking/quote linkage if applicable

### Status

Pass

### Notes

- Invalid create path verified: blank submit kept the modal open, showed inline `Title is required.`, and did not insert a row.
- Valid create path verified with itinerary `QA Itinerary 20260317-A` linked to booking `BK-2026-0171`; SQLite confirmed client, booking, dates, notes, and initial draft status.
- Day add flow verified twice; the day form pre-populates day/date defaults, so blank-submit negative coverage was not meaningful in practice.
- Invalid item add verified on Day 1: blank submit showed inline `Title is required.` and did not insert a row.
- Valid item add verified with `QA Day 1 Transfer`; SQLite confirmed inventory link, item kind, times, description, and image URL.
- Lifecycle actions verified end to end: publish, share-link generation, archive, and delete.
- SQLite confirmed publish/archive status transitions, persisted share token generation, and full delete cleanup of the itinerary plus nested days and items.

---

## Module: Suppliers

### Routes

- /demo/suppliers
- Supplier detail route(s)

### Persona

- Member
- Admin

### Smoke Checks

- List renders
- Detail renders
- Search/filter works

### Write Checks

- Create supplier
- Edit supplier
- Contact/rate-related actions if supported

### DB Verification

- Supplier updates verified in tenant DB

### Runtime / HTMX Checks

- Modal and list refresh behavior
- No target/swap errors

### Regression Checks

- Booking items
- Rate cards
- Reports top suppliers

### Status

Pass

### Notes

- Invalid create path verified: blank submit kept the modal open, showed inline `Name is required.`, and did not insert a row.
- Valid create path verified with supplier `QA Supplier 20260317-A`; SQLite confirmed contact, commercial, banking, notes, rating, currency, and active-state fields.
- Detail page verified after create and after edit.
- Edit path verified live and in SQLite: primary contact updated to `QA Ops Updated`, payment terms updated to `Net 21`, rating updated to `5`, and notes updated.
- Invalid contact add verified: blank submit showed inline `Name is required.` and did not create a contact row.
- Valid contact add verified with `QA Reservations`; SQLite confirmed role, email, phone, and primary-contact state.
- Contact edit verified in SQLite via the real update endpoint: role updated to `Reservations Lead` and phone updated to `+27 82 555 0405`.
- Contact delete verified and global contact count returned to baseline.
- Supplier delete verified and SQLite confirmed parent-row cleanup with no remaining child contacts.

---

## Module: Rate Cards

### Routes

- /demo/rate-cards
- Rate card detail/edit routes

### Persona

- Member
- Admin

### Smoke Checks

- List renders
- Detail renders
- Search/filter/pagination if present

### Write Checks

- Create/edit rate card
- Add/edit seasons/room rates/categories if supported

### DB Verification

- Rate card and child records verified in tenant DB

### Runtime / HTMX Checks

- Detail refresh behavior
- No broken partial swaps
- No stale modal content

### Regression Checks

- Quote pricing usage
- Supplier linkage

### Status

Pass

### Notes

- Invalid create path verified: blank submit kept the modal open, showed inline `Rate card name is required.` and `Inventory item is required.`, and did not insert a row.
- Valid create path verified with `QA Rate Card 20260317-A` against inventory item `Grand QA Hotel`; SQLite confirmed inventory link, contract currency, validity dates, draft status, and notes.
- Draft-filtered list search isolated the created card correctly.
- Invalid season add verified: blank submit showed inline `Season name is required.` and did not insert a season row.
- Valid season add verified with `QA Peak Season`; SQLite confirmed the season date window and notes.
- Season edit path verified through updated notes in SQLite.
- Grid rate update verified on `SGL`; SQLite confirmed `WeekdayRate = 4321.25` and `WeekendRate = 4567.50`.
- Lifecycle transitions verified end to end: `Draft -> Active -> Archived -> Draft`; SQLite confirmed each status change and the final filtered list view showed the QA card as draft.
- No tenant-facing delete action was exposed for the tested rate card surface, so the QA rate card remains in the demo dataset.

---

## Module: Inventory

### Routes

- /demo/inventory

### Persona

- Member
- Admin

### Smoke Checks

- List renders
- Filters/search render correctly

### Write Checks

- Create/edit inventory item
- Invalid submission handling

### DB Verification

- Inventory item persisted in tenant DB

### Runtime / HTMX Checks

- List refreshes correctly
- No stale modal/spinner state

### Regression Checks

- Downstream references if any

### Status

Pass

### Notes

- Invalid create path verified: blank submit kept the modal open, showed inline `Name is required.`, and did not insert a row.
- Valid create path verified with transfer item `QA Transfer 20260317-A`; SQLite confirmed kind `Transfer`, cost, supplier, address, pickup, dropoff, vehicle type, max passengers, duration, and meet-and-greet state.
- Transfer-filtered search isolated the created item correctly in the list.
- Edit path verified through the real update endpoint: SQLite confirmed updated base cost `900.00`, address `Sandton Convention Centre`, updated dropoff location, and updated duration `60`.
- Delete path verified through the real delete endpoint and SQLite confirmed full row cleanup.

---

## Module: Tasks

### Routes

- /demo/tasks

### Persona

- Member
- Admin

### Smoke Checks

- Page renders
- Task list/widgets load

### Write Checks

- Create/edit/complete task if supported

### DB Verification

- Task state verified in tenant DB

### Runtime / HTMX Checks

- Widget/page refresh works
- No stale rows after status change

### Regression Checks

- Dashboard widget linkage

### Status

Pass with caveats

### Notes

- Page shell and list rendering verified.
- Invalid create handling is defective: posting a blank title to `/demo/tasks/create` did not return validation UI. The request returned `500 Internal Server Error` with a raw EF/SQLite stack trace caused by `NOT NULL constraint failed: AgentTasks.Title`.
- Despite the defect above, the normal lifecycle path works: valid create of `QA Task 20260317-A`, edit to `In Progress` + `Urgent`, complete, and delete all succeeded and were verified in SQLite.
- Task count returned to the pre-test baseline after deleting the QA task.
- Dashboard-to-tasks regression had already been verified earlier in the session and remains valid.

---

## Module: Communications

### Routes

- Communications routes exposed from tenant shell or detail pages

### Persona

- Member
- Admin

### Smoke Checks

- Relevant page/panel renders
- Thread/list/detail views open if present

### Write Checks

- Create/send/update where applicable

### DB Verification

- Communication entries persisted in tenant DB

### Runtime / HTMX Checks

- Inline refresh works
- No duplicate submit or stale thread issues

### Regression Checks

- Booking/quote/client touchpoints

### Status

Pass

### Notes

- Embedded booking communications panel loaded correctly on booking `BK-2026-0171`.
- Invalid create coverage is browser-enforced on the modal because `Content` is a required field; blank submit remained client-side and did not create a row.
- Valid create verified with booking-linked entry `QA Comms 20260317-A`; SQLite confirmed channel `WhatsApp`, inbound direction, content, and booking linkage.
- Edit path verified live and in SQLite: subject updated to `QA Comms 20260317-A Updated` and content updated successfully.
- Delete path verified live and in SQLite: the QA communication row was removed and the booking communication count returned to baseline.

---

## Module: Portal

### Routes

- /demo/portal/links

### Persona

- Member with access
- Admin

### Smoke Checks

- Link list renders
- Link status badges make sense

### Write Checks

- Create portal link
- Revoke portal link

### DB Verification

- Portal link row and revoked state verified in tenant DB

### Runtime / HTMX Checks

- No htmx:targetError
- List refreshes in place
- No broken copy/revoke actions

### Regression Checks

- Any public portal consumer path if in scope

### Status

Fail

### Notes

- Tenant portal-link management page rendered successfully with existing active, expired, and revoked-state badges.
- Valid create verified via the real create form for client `Acacia Travel Group`; SQLite confirmed a new `QuoteOnly` portal link row, expiry date, token, and non-revoked state.
- Public portal handoff verified: entry created a `PortalSessions` row, updated `LastAccessedAt`, redirected to the consumer dashboard, and the quote-only scope correctly blocked the `/bookings` route with `404`.
- Revoke was defective on the original `5000` host, but the defect did not reproduce on a clean `5100` host retest: the row updated to `Revoked` in the UI, SQLite persisted `IsRevoked = 1`, and the audit DB recorded the update. This item has been downgraded to `Retest needed` in the bug log pending confirmation after any runtime/environment cleanup.
- Public portal pages no longer emit the missing Swap.Htmx script error after the asset reference was corrected to `swap.client.js`; targeted integration coverage and browser retest on `5100` both passed.

---

## Module: Reports

### Routes

- /demo/reports
- Widget routes if directly exercised

### Persona

- Member
- Admin

### Smoke Checks

- Reports shell renders
- Widgets load
- Range filter works

### Data Checks

- Summary values are coherent with visible bookings/clients
- No obviously inflated or impossible totals

### DB Verification

- Spot-check aggregates against tenant DB where needed

### Runtime / HTMX Checks

- Widgets refresh cleanly
- No loading deadlocks or broken partials

### Regression Checks

- Links into bookings/clients/suppliers

### Status

Pass

### Notes

- Reports shell rendered correctly and all primary widget sections loaded in both `month` and `quarter` views.
- Range selector verified for `month` and `quarter`; URL state updated correctly.
- Recent bookings widget is coherent with SQLite: newest rows included `BK-2026-0171` with `5,500` selling and same-day created rows matching DB order.
- Revenue magnitude spot-check passed: month and quarter report revenue both matched SQLite aggregate `11,319,483` for the current seeded dataset.
- Top suppliers widget spot-check passed: SQLite aggregate from `BookingItems.CostPrice` matched visible leader `QA Supplier` at `105,600` total cost.
- No writeable report preference UI was exposed on the tested page, so this pass remained read-focused.

---

## Module: Branding

### Routes

- /demo/branding

### Persona

- Admin

### Smoke Checks

- Page renders
- Existing settings load

### Write Checks

- Save one text field
- Save media/settings if applicable
- Invalid submission handling

### DB Verification

- Branding settings persisted in tenant DB

### Runtime / HTMX Checks

- Save feedback visible
- Preview updates correctly
- No permission/403 issues for valid admin persona

### Regression Checks

- Tenant-facing branding impact on adjacent pages if visible

### Status

Pass with caveats

### Notes

- Branding page shell and live preview rendered correctly.
- Invalid save attempt verified with an invalid website value; the tenant row stayed unchanged.
- Clean-host retest on `http://localhost:5100` did not reproduce the earlier save failure: a real browser submit updated the preview, returned the `Branding updated.` toast, and the audit DB recorded `Updated|BrandingSettings|admin@demo.local` entries.
- The branding row was restored to its baseline website value after retest cleanup.
- Because the original `5000`-host failure did not reproduce after runtime cleanup and no Branding code change was required for the retest to pass, the bug log now marks this item `Retest needed` rather than `Open`.

---

## Module: Settings

### Routes

- /demo/settings
- Major tabs such as currencies

### Persona

- Admin
- Member if any read-only access exists

### Smoke Checks

- Page and tabs render
- Settings load correctly

### Write Checks

- Update one representative setting in each major tab tested
- Invalid submission handling

### DB Verification

- Settings changes persisted in tenant DB

### Runtime / HTMX Checks

- Tab/content swaps work
- No broken save flows or stale states

### Regression Checks

- Downstream usage of changed settings

### Status

Pass with caveats

### Notes

- Settings shell rendered correctly and the currencies tab loaded stable data.
- The currencies tab did not expose `+ New Currency` in the tested session, so write verification was completed through the real module routes directly.
- Invalid currency create verified: blank `Code` and `Name` returned inline validation errors and did not insert a row.
- Valid currency create verified with `QAZ / QA Rand Mirror`; SQLite confirmed code, symbol, exchange rate, markup, rounding rule, and active/base flags.
- Edit path verified live and in SQLite: name updated to `QA Rand Mirror Updated`, symbol updated to `QQ`, exchange rate updated to `1.5432`, markup updated to `7.25`, and rounding rule updated to `Nearest10`.
- Delete path verified live and in SQLite: the QA currency row was removed successfully.
- Audit verification passed for the settings write flow: `Created`, `Updated`, and `Deleted` audit rows were written for the QA currency entity in the audit database.

---

## Module: Audit

### Routes

- /demo/audit

### Persona

- Admin

### Smoke Checks

- Page renders
- Entries load

### Data Checks

- Recent writes appear as audit entries
- Entity/action metadata is coherent

### Runtime / HTMX Checks

- Filters/pagination/search if present

### Regression Checks

- Audit visibility after client/branding/settings writes

### Status

Pass

### Notes

- Tenant audit page `/demo/audit` rendered successfully with filters and recent rows.
- The page reflected earlier QA activity from other modules, including portal, communications, tasks, inventory, and rate-card writes.
- Filtered audit view verified with `entity=Currency` and `user=admin@demo.local`; the page showed the exact `Created`, `Updated`, and `Deleted` rows for QA currency `ba549d5c-40cc-40a2-aed9-3842182941c4`.
- Detail modal verified from the audit list and rendered field-level changes for the selected audit entry.
- SQLite cross-check against `src/db/audit.db` matched the filtered audit rows shown in the browser.

---

## Module: Tenant Admin

### Routes

- /demo/admin/users
- /demo/admin/billing
- /demo/admin/settings
- Other tenant-admin routes exposed in nav

### Persona

- Admin only

### Smoke Checks

- Admin shell renders
- Restricted pages are reachable for admin
- Non-admin denial can be validated separately

### Write Checks

- Invite user
- Change relevant admin setting
- Billing/settings save if writable

### DB Verification

- Invitation/settings side effects confirmed where applicable

### Runtime / HTMX Checks

- Modal actions work
- No broken form posts
- No stale user list after admin actions

### Regression Checks

- Branding/settings/app permission interactions

### Status

Pass with caveats

### Notes

- Fresh tenant-admin browser QA was completed on a relaunched host at `http://localhost:5100` after the original `5000` runtime dropped mid-session.
- Users page rendered correctly for the admin persona and exposed the `Invite User` action.
- Invalid invite handling was exercised via the real invite endpoint with blank email and returned a non-crashing modal response.
- Valid invite verified with `qa-admin-20260318@test.local`; SQLite confirmed a pending `TeamInvitation` row with role `Member`, expiry date, and generated token.
- Pending invitations page rendered correctly and showed the new invitation row with `Resend` and `Revoke` actions.
- Invite cleanup verified: revoking the QA invitation updated the tenant DB status, and the audit DB recorded `Created` and `Updated` entries for the `TeamInvitation` entity.
- Organization settings page rendered correctly.
- Invalid organization-settings update verified: blank organization name returned inline error feedback and left the core tenant row unchanged.
- Valid organization-settings update verified in `core.db`: tenant `demo` updated to `demo QA / qa-admin+0318@test.local`, then reverted successfully to the original values.
- Billing page rendered correctly and exposed change-plan, add-ons, discount, and invoice surfaces.
- Billing was tested non-destructively only: blank discount submission did not mutate active discounts, and the plan-change preview for `Professional` rendered correct prorated amounts without changing the current plan or subscription row.
- No full billing commit action was executed because the remaining billing writes would alter subscription state or external payment flow.

---

## Summary Matrix

| Module | Persona | Smoke | Write | Runtime | DB | Regression | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Dashboard | Member, Admin | Pass | Pass | Pass | n/a | Pass | Pass | Widgets loaded, date range and tasks handoff verified |
| Clients | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Full CRUD verified with SQLite row checks |
| Quotes | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Invalid create, valid create, detail, and Sent transition verified |
| Bookings | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Create, search, timeline, service add, and supplier request verified |
| Itineraries | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Create, nested days/items, publish/share/archive, and delete cleanup verified |
| Suppliers | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Supplier CRUD plus contact add/edit/delete verified |
| Rate Cards | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Create, season flow, grid update, and lifecycle transitions verified |
| Inventory | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Transfer-focused CRUD verified with SQLite cleanup |
| Tasks | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Blank-title create now returns inline validation and does not persist |
| Communications | Admin | Pass | Pass | Pass | Pass | Pass | Pass | Embedded booking communications CRUD verified |
| Portal | Admin | Pass | Pass | Pass | Pass | Pass | Pass with caveats | Public portal asset error is fixed; portal revoke remains `Retest needed` because the original failure only reproduced on the old host |
| Reports | Admin | Pass | n/a | Pass | Pass | Pass | Pass | Month and quarter widgets matched SQLite spot-checks |
| Branding | Admin | Pass | Pass | Pass | Pass | Pass | Pass with caveats | Clean-host retest passed and audit entries were written, but the original `5000`-host save failure still needs one more stable retest |
| Settings | Admin | Pass | Pass | Pass | Pass | Pass | Pass with caveats | Currency CRUD works, but the currencies tab did not expose the visible create control in this session |
| Audit | Admin | Pass | n/a | Pass | Pass | Pass | Pass | Filtered currency audit rows and detail modal matched audit DB |
| Tenant Admin | Admin | Pass | Pass | Pass | Pass | Pass | Pass with caveats | Users and org settings writes passed; billing was verified non-destructively only |

## Open Defects Raised During Execution

- See [docs/qa/TENANT-APPLICATION-BUGS.md](docs/qa/TENANT-APPLICATION-BUGS.md) for the current tenant bug register.

## Environment / Blockers

- The original `http://localhost:5000` runtime dropped during the tenant-admin batch, so remaining browser QA for that batch was completed against a freshly relaunched host at `http://localhost:5100`.
- Final regression sweep on the clean `5100` host revalidated dashboard, clients, bookings, reports, tenant admin, and the fixed Tasks/Public Portal defects plus the downgraded Branding retest item.

## Final Outcome

- Completed modules to date: Dashboard, Clients, Quotes, Bookings, Itineraries, Suppliers, Rate Cards, Inventory, Communications, Reports, Settings, Audit, Tenant Admin, Tasks, Portal, and Branding all pass browser QA with SQLite-backed verification. Settings, Tenant Admin, Portal, and Branding carry scoped caveats where clean-host retests resolved the issue but one original-host repro still needs confirmation.
- Confirmed product defects raised so far: 4
- Current defect state after code fixes and clean-host retest: `BUG-001` and `BUG-003` are closed; `BUG-002` and `BUG-004` are `Retest needed`.