# UX Backlog

Items are ordered by priority. Work through them top to bottom. Each item requires: build pass + unit tests + integration tests + Playwright browser QA + commit before moving to the next.

Completed items are marked ✅.

---

## Completed

- **QT-2** — Rate card search in quote builder (client-side live filter with empty state) ✅
- **CL-HISTORY** — Recent bookings and recent quotes sections in client details modal ✅

---

## P0 — Critical

### D-1 — Dashboard Widgets ✅
Already implemented: Tasks widget, Active Bookings, Quote Pipeline, Revenue summary, and Recent Bookings all lazy-load via `hx-trigger="load"` behind feature/permission gates. Quick actions (New Booking, New Quote, New Client) present in the welcome card.

---

## P1 — High

### HX-1 — OOB Swaps for Booking Mutations ✅
Already resolved: each mutation (CreateItem, CreatePayment, ConfirmSupplier, CreateSupplierPayment) emits exactly one event trigger. `#booking-summary` listens to `BookingEvents.ItemsRefresh` without `load` so it only refreshes on mutation, not on page load. No multi-trigger cascade exists.

### HX-2 + PF-1 — Booking Details Load Optimization ✅
Already resolved: `#booking-summary` is server-rendered inline. Items, Payment Links, Payments, Documents load on `load`. Team, Comments, Activity, Communications all use `hx-trigger="revealed"`. Only 4 parallel fetches occur on page load.

### RP-1 — Reports Charts ✅
Revenue, bookings-by-status, and quotes-pipeline report widgets render data as plain text/tables. Add Chart.js visualizations (bar for revenue, doughnut for status, line for pipeline trend). Charts must be server-data-driven via a `data-chart-config` JSON attribute — no extra API calls.

Target files:
- `src/Modules/Reports/Views/Report/_RevenueMonthly.cshtml`
- `src/Modules/Reports/Views/Report/_BookingsStatus.cshtml`
- `src/Modules/Reports/Views/Report/_QuotesPipeline.cshtml`
- `src/Modules/Reports/Controllers/ReportController.cs`

### ✅ CL-1 — Client Full Profile Page
The client module has a details modal but no dedicated full-profile page. Add a `Profile` action that renders a `SwapView` with:
- Contact info panel
- Linked bookings table (paginated, 10 per page)
- Linked quotes table (paginated, 10 per page)
- Documents section
- "View Full Profile" link from the details modal

Target files:
- `src/Modules/Clients/Controllers/ClientController.cs`
- New: `src/Modules/Clients/Views/Client/Profile.cshtml`

---

## P2 — Medium

### ✅ ST-2 — Settings Tab Search
The destinations and suppliers tabs in Settings have no filtering. Add a client-side search input per tab that filters visible rows by name. Same pattern as QT-2 (data attribute + input event listener).

Target files:
- `src/Modules/Settings/Views/Settings/Index.cshtml` (or relevant partials)

### ✅ RC-1 — Rate Card Batch Save
Editing rate card prices currently saves each cell change individually, causing N+1 POST requests. Add dirty-state tracking per row and a single "Save Changes" button that batches all modified rows in one POST.

Target files:
- `src/Modules/RateCards/Views/RateCard/Index.cshtml`
- `src/Modules/RateCards/Controllers/RateCardController.cs`

### ✅ PF-2 — User Name Resolver Service
Multiple partials resolve user display names with ad-hoc queries inside loops (N+1 pattern). Introduce a scoped `IUserNameResolver` that bulk-loads all required names once per request and serves them from a dictionary.

Target files:
- New: `src/Infrastructure/Services/UserNameResolver.cs`
- Register in `src/Infrastructure/ServiceCollectionExtensions.cs`
- Update callers in bookings, quotes, tasks modules

### ✅ PF-3 — Sidebar Feature Flag Caching
The sidebar evaluates feature flags on every render via individual DB calls. Load all flags for the current tenant once per request in middleware and cache in `HttpContext.Items`.

Target files:
- `src/Infrastructure/Middleware/` (new middleware or extend existing)
- `src/Modules/FeatureFlags/Services/FeatureFlagService.cs`

### ✅ PF-8 — Communications Pagination
The communications tab on booking details loads all records with no limit, causing unbounded query results for clients with high message volume. Add default `Take(20)` with a "Load more" HTMX pattern.

Target files:
- `src/Modules/Communications/Controllers/CommunicationsController.cs`
- `src/Modules/Communications/Views/Communications/_List.cshtml`

---

## P3 — Polish

### ✅ BK-4 — Booking Calendar / Timeline View
Add a timeline or calendar view to the bookings index, toggled alongside the existing table view. Show bookings plotted by travel dates.

### QT-3 — Quote Version Comparison
When a quote has multiple versions, add a side-by-side or diff view comparing line items and totals between any two selected versions.

### TK-2 — Task Relative Due Dates
Tasks list shows absolute dates. Add relative display ("due today", "2 days overdue", "due in 3 days") with color coding (red = overdue, amber = due today, grey = future).




