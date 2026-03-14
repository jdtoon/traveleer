# 01 — Navigation Improvements

## Current State

The sidebar in `_TenantLayout.cshtml` lists modules in a flat structure:
Dashboard, Branding, Bookings, Clients, Inventory, Quotes, Rate Cards, Settings, Audit Log.

Below that: Account section (Profile & 2FA, Sessions), Admin button, Sign Out.

### What Works
- Feature-flag gating hides modules the tenant hasn't enabled.
- Permission checks hide items the user can't access.
- Active state (`active` class) highlights the current controller.
- Mobile drawer closes on nav click.
- `swap-nav` handles HTMX boosted navigation targeting `#main-content`.

### What's Missing or Broken

---

## N-1. Missing Modules from Sidebar Navigation

**Priority: P0**

Several active modules have no sidebar entry:

| Module | Has Routes | In Sidebar |
|--------|-----------|------------|
| Suppliers | `/{slug}/suppliers` | **No** |
| Itineraries | `/{slug}/itineraries` | **No** |
| Tasks | `/{slug}/tasks` | **No** |
| Reports | `/{slug}/reports` | **No** |
| Portal (admin links) | `/{slug}/portal/links` | **No** |
| Communications | embedded in other modules | N/A (correct) |
| Email | embedded in quotes | N/A (correct) |

**Fix**: Add `Suppliers`, `Itineraries`, `Tasks`, `Reports`, and `Portal` to the sidebar with appropriate feature-flag and permission checks.

**Suggested sidebar order** (group by workflow stage):
```
Dashboard
─── Sales ───
  Clients
  Quotes
  Bookings
  Itineraries
─── Operations ───
  Suppliers
  Inventory
  Rate Cards
  Tasks
─── Insights ───
  Reports
  Portal
─── Configuration ───
  Branding
  Settings
  Audit Log
```

---

## N-2. No Sidebar Grouping or Section Headers

**Priority: P1**

All modules are in a flat list. With 12+ items, users lose context. DaisyUI menus support `menu-title` elements for grouping (already used for the "Account" section).

**Fix**: Add `<li class="menu-title"><span>Sales</span></li>` etc. before each group. This is a pure markup change in `_TenantLayout.cshtml`.

---

## N-3. Active State Doesn't Cover Sub-Controllers

**Priority: P1**

Active highlighting uses exact controller name matching:
```html
class="@(controller == "Booking" ? "active" : "")"
```

But the Bookings detail page loads sub-controllers (Document, Payment, Collaboration) whose controller name differs. When viewing a booking's documents, the Bookings sidebar item loses its active state.

Similarly:
- Supplier contacts (`SupplierContact` controller) — Suppliers loses active
- Quote email (`QuoteEmail` controller) — Quotes loses active
- Portal links (`PortalLink` controller) — no sidebar item at all

**Fix**: Change active matching to check route prefix instead of exact controller:
```csharp
var path = Context.Request.Path.ToString().ToLower();
// Then: class="@(path.Contains("/bookings") ? "active" : "")"
```

Or maintain a `ViewData["ActiveNav"]` convention set per controller.

---

## N-4. No Breadcrumbs in Tenant App Layout

**Priority: P1**

`_TenantAdminLayout.cshtml` has breadcrumbs (`TenantName → Admin → Section`), but the main `_TenantLayout.cshtml` has none. Users who navigate deep into Bookings → Details → Items have no context trail.

**Fix**: Add a breadcrumb bar below the mobile navbar in `_TenantLayout.cshtml`:
```html
<div class="text-sm breadcrumbs px-4 md:px-6 pt-2">
    <ul>
        <li><a href="/@slug">Dashboard</a></li>
        @if (ViewData["Breadcrumb"] is string bc)
        {
            <li>@bc</li>
        }
    </ul>
</div>
```

Controllers should set `ViewData["Breadcrumb"]` (e.g., "Bookings", "BK-2026-0012 Details").

---

## N-5. Booking Detail Page Has No Back Link in Summary Partial

**Priority: P1**

The booking details page loads `_Summary` via HTMX, which contains a "Back to Bookings" link. But when the summary refreshes via OOB trigger, the back link persists. However, if a user navigates directly to `/bookings/details/{id}` (e.g., via browser bookmark), there's no indication of where they came from.

**Fix**: Ensure every detail page (Bookings, Quotes, Itineraries, Suppliers, Rate Cards) has a consistent back-link pattern in its summary section. Consider making back-links a `ViewData["BackLink"]` convention rendered in the breadcrumb bar.

---

## N-6. Settings Tab State Lost on Browser Back

**Priority: P2**

Settings uses JavaScript `switchSettingsTab()` to show/hide tab content. The active tab is not reflected in the URL, so:
- Browser back button doesn't restore the tab
- Sharing a link always opens Room Types (first tab)
- Refreshing the page resets to the first tab

**Fix**: Use URL query params (`?tab=currencies`) and read them on page load. The tab switch function should update `history.replaceState`.

---

## N-7. Tasks Widget Not Connected to Dashboard

**Priority: P1**

The Tasks module has a `_Widget.cshtml` partial and a `Widget()` controller action that returns overdue count + upcoming tasks. But the Dashboard (`Index.cshtml`) doesn't load it. The widget exists but isn't wired up.

**Fix**: Dashboard should load task widget, recent bookings, and quote pipeline as HTMX partials.

---

## N-8. Quote Details → Booking Link Uses Different Pattern

**Priority: P2**

Quote details show "View Booking" as a link when a booking exists. But Booking details show "View Quote" only in certain scenarios. The cross-referencing is inconsistent.

**Fix**: Both detail pages should consistently show the linked entity when a relationship exists, using the same visual pattern (badge + link).
