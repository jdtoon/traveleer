# 07 — Empty States and Error States

## Current State

Most modules have empty states with contextual messaging. The pattern is consistent:
```html
<div class="rounded-box border border-dashed border-base-300 bg-base-200/40 p-8 text-center">
    <h3 class="text-lg font-semibold">No items yet</h3>
    <p class="mt-2 opacity-70">Help text explaining what to do.</p>
</div>
```

### Empty State Audit

| Module | List Empty | Detail Empty | Filter Empty | Quality |
|--------|-----------|-------------|-------------|---------|
| Bookings | Yes + CTA text | N/A | Yes (filter-specific) | Good |
| Clients | Yes + CTA text | N/A | Needs check | OK |
| Quotes | Yes + CTA text | N/A | Yes | Good |
| Inventory | Yes + contextual | N/A | Yes (filter vs empty) | Good |
| Rate Cards | Yes + CTA text | Yes (no seasons) | Yes | Good |
| Suppliers | Yes | Contact list empty | Needs check | OK |
| Itineraries | Yes | Days empty | Needs check | OK |
| Tasks | Needs check | N/A | Needs check | Unknown |
| Settings tabs | Needs check per tab | N/A | N/A | Unknown |
| Communications | Yes | N/A | N/A | OK |
| Reports | N/A (widgets) | N/A | N/A | N/A |
| Portal | Needs check | No bookings/quotes | N/A | Unknown |
| Dashboard | N/A (welcome only) | N/A | N/A | Poor |

---

## ES-1. Empty Dashboard Provides No Guidance

**Priority: P1**

After onboarding, the dashboard shows only "Welcome to {TenantName}". A new user doesn't know what to do next.

**Fix**: Show a setup checklist or getting-started card when the tenant has no data:

```html
<div class="card bg-base-100 border border-base-300">
    <div class="card-body">
        <h3 class="card-title">Get started with Traveleer</h3>
        <div class="steps steps-vertical">
            <div class="step @(hasSettings ? "step-primary" : "")">
                <a href="/@slug/settings">Configure your settings</a>
            </div>
            <div class="step @(hasInventory ? "step-primary" : "")">
                <a href="/@slug/inventory">Add your first hotel or product</a>
            </div>
            <div class="step @(hasRateCard ? "step-primary" : "")">
                <a href="/@slug/rate-cards">Create a rate card</a>
            </div>
            <div class="step @(hasClient ? "step-primary" : "")">
                <a href="/@slug/clients">Add a client</a>
            </div>
            <div class="step @(hasQuote ? "step-primary" : "")">
                <a href="/@slug/quotes">Build your first quote</a>
            </div>
        </div>
    </div>
</div>
```

Once the tenant has sufficient data, replace with the dashboard widgets (see 02-DASHBOARD.md).

---

## ES-2. Tasks Module Missing Empty State

**Priority: P2**

The Tasks `_TaskList.cshtml` needs verification for empty state handling when no tasks match the current filter. If missing, add:

```html
@if (tasks.Count == 0)
{
    <div class="rounded-box border border-dashed ...">
        @if (hasActiveFilters)
        {
            <p>No tasks match this filter. Try adjusting the status or assignee.</p>
        }
        else
        {
            <p>No tasks yet. Tasks help track follow-ups for bookings, quotes, and supplier confirmations.</p>
        }
    </div>
}
```

---

## ES-3. Settings Tabs Need Individual Empty States

**Priority: P2**

Each settings tab should have its own empty state with appropriate CTA text:

| Tab | Empty Message |
|-----|--------------|
| Room Types | "No room types configured. Add SGL, DBL, TWN to get started." |
| Meal Plans | "No meal plans added. Common plans include BB, HB, FB, AI." |
| Currencies | "No currencies set up. Add your base and common selling currencies." |
| Destinations | "No destinations yet. Add the cities and regions you operate in." |
| Rate Categories | "No rate categories. These are used for non-hotel pricing dimensions." |

Settings entities are seeded on tenant creation, so these empty states should only appear if an admin deletes all items.

---

## ES-4. Portal Public Pages Need Friendly Empty States

**Priority: P2**

When a client accesses the portal and has no bookings or quotes, the portal should show a friendly message rather than an empty list or blank page.

```html
<div class="text-center py-12">
    <h3 class="text-xl font-semibold">No bookings yet</h3>
    <p class="opacity-70 mt-2">Your travel agent is preparing your trip details. Check back soon!</p>
</div>
```

---

## ES-5. Error Responses Don't Show Recovery Actions

**Priority: P2**

When a service operation fails (e.g., Create returns an error), the controller shows an error toast. But the toast disappears after 5 seconds and the user is left on the same page with no clear next step.

**Fix**: For important errors, return both a toast AND re-render the form with the error context:

```csharp
// Already done for validation errors — extend to business logic errors:
if (!result.Success)
{
    ModelState.AddModelError("", result.ErrorMessage ?? "Operation failed.");
    return SwapResponse()
        .WithErrorToast(result.ErrorMessage ?? "Operation failed.")
        .WithView("_Form", dto)  // Keep the form open with user's data
        .Build();
}
```

---

## ES-6. 404 Pages Within Tenant Context

**Priority: P2**

When a user navigates to a non-existent entity (e.g., `/demo/bookings/details/{invalid-guid}`), controllers return `NotFound()`. The default 404 page may not match the tenant layout.

**Fix**: Verify that 404, 403, and 500 error pages render within `_TenantLayout.cshtml` for tenant routes. Create tenant-context-aware error partials if they're currently falling back to the public error pages.

---

## ES-7. Booking Empty State Missing Onboarding Path

**Priority: P2**

The booking list empty state says: "Create your first booking once a client and inventory items are ready for operations."

But it doesn't tell the user HOW to get clients and inventory items ready. It should link to those modules:

```html
<p class="mt-2 opacity-70">
    Create your first booking once 
    <a href="/@slug/clients" class="link">a client</a> and 
    <a href="/@slug/inventory" class="link">inventory items</a> 
    are ready for operations.
</p>
```
