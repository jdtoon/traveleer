# 02 — Dashboard Improvements

## Current State

The Dashboard module (`src/Modules/Dashboard/`) is the tenant landing page at `/{slug}`. It currently:

1. Checks `IOnboardingService.ShouldRedirectToOnboardingAsync()` and redirects new tenants.
2. Renders a static welcome card with the tenant name.
3. Has no data loading, no widgets, no HTMX partials.

The Dashboard is the first thing users see after login. It should orient them toward their work.

---

## D-1. Dashboard Is Empty — No At-a-Glance Data

**Priority: P0**

After onboarding, the dashboard shows only "Welcome to {TenantName}". Users immediately navigate away to Bookings or Quotes. The dashboard provides no value.

**Fix**: Add lazy-loaded HTMX widgets to the dashboard. Each widget loads independently with its own spinner, keeping the page fast.

### Proposed Widget Layout

```
┌─────────────────────────────────────────────────────────┐
│ Dashboard                                    [date range]│
├──────────────┬──────────────┬──────────────┬────────────┤
│ Active       │ Quotes       │ Revenue      │ Overdue    │
│ Bookings     │ Pipeline     │ This Month   │ Tasks      │
│    12        │   8 → 3 → 2 │  $24,500     │    3       │
├──────────────┴──────────────┴──────────────┴────────────┤
│ Recent Bookings                     [View All →]        │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ BK-2026-0012  Acme Corp  Confirmed  $4,200  Mar 12 │ │
│ │ BK-2026-0011  Beta LLC   Provisional $1,800 Mar 10 │ │
│ │ ...                                                 │ │
│ └─────────────────────────────────────────────────────┘ │
├─────────────────────────────┬───────────────────────────┤
│ Upcoming Tasks              │ Quote Activity            │
│ ┌─────────────────────────┐ │ ┌───────────────────────┐ │
│ │ ⚠ Confirm supplier      │ │ │ QT-0042 Sent → Client │ │
│ │   Due: Tomorrow         │ │ │ QT-0041 Draft created │ │
│ │ ○ Follow up payment     │ │ │ QT-0040 → Accepted    │ │
│ │   Due: Mar 18           │ │ └───────────────────────┘ │
│ └─────────────────────────┘ │                           │
└─────────────────────────────┴───────────────────────────┘
```

### Implementation

The Reports module already has these service methods:
- `GetRecentBookingsAsync()` — last 10 bookings
- `GetBookingsByStatusAsync()` — status breakdown
- `GetQuotePipelineAsync()` — quotes by status
- `GetProfitabilitySummaryAsync()` — revenue summary

The Tasks module has:
- `GetWidgetDataAsync()` — overdue count + 5 upcoming tasks

**Reuse these existing services** in new DashboardController endpoints:

```csharp
[HttpGet("widgets/bookings-summary")]
public async Task<IActionResult> BookingsSummary() { ... }

[HttpGet("widgets/tasks")]
public async Task<IActionResult> TasksWidget() { ... }

[HttpGet("widgets/recent-bookings")]
public async Task<IActionResult> RecentBookings() { ... }

[HttpGet("widgets/quote-pipeline")]
public async Task<IActionResult> QuotePipeline() { ... }
```

Each widget rendered as a card partial loaded via `hx-trigger="load"`.

---

## D-2. No Quick Actions on Dashboard

**Priority: P1**

Users should be able to start common tasks from the dashboard:
- "+ New Booking" — opens booking form modal
- "+ New Quote" — navigates to quote builder
- "+ New Client" — opens client form modal

**Fix**: Add a row of quick-action buttons at the top of the dashboard, gated by permissions:
```html
<has-permission name="bookings.create">
    <button class="btn btn-primary btn-sm" hx-get="/{slug}/bookings/new" hx-target="#modal-container">
        + New Booking
    </button>
</has-permission>
```

---

## D-3. No Date Range Control

**Priority: P2**

Reports module has a date range dropdown (This Month / This Quarter / This Year). Dashboard widgets should share the same convention.

**Fix**: Add a date range selector at the top-right of the dashboard. Pass the range as a query param to each widget's `hx-get` URL.

---

## D-4. No "View All" Links from Dashboard Widgets

**Priority: P1**

Each widget should link to its full module:
- Recent Bookings → "View All" links to `/bookings`
- Quote Pipeline → "View All" links to `/quotes`
- Tasks → "View All" links to `/tasks`
- Reports card → links to `/reports`

This creates natural entry points and reduces sidebar-only navigation dependency.

---

## D-5. Onboarding Redirect Check on Every Dashboard Load

**Priority: P2**

The `DashboardController.Index()` calls `IOnboardingService.ShouldRedirectToOnboardingAsync()` on every load. Once onboarding is complete, this check still runs. If the check is a database query, it adds latency to every dashboard visit.

**Fix**: Cache the onboarding completion flag in the tenant session or feature flags so the check is a memory lookup after the first time.
