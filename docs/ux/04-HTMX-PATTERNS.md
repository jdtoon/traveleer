# 04 — HTMX Patterns and OOB Swaps

## Current State

The app uses Swap.Htmx with `AutoSuppressLayout = true` and `DefaultNavigationTarget = "#main-content"`. HTMX patterns include:
- `hx-trigger="load, {event} from:body"` for lazy-loading and event-driven refreshes
- `SwapResponse().WithTrigger().WithView().Build()` for compound responses
- Modal forms targeting `#modal-container`
- Toast notifications via `#toast-container`

### What Works Well
- Consistent modal open/close pattern across all modules.
- Event-driven list refresh after CRUD operations.
- Loading spinners in all HTMX-loaded containers.
- Layout suppression for partials is automatic.

---

## HX-1. No OOB Swaps Used — Missed Optimization Opportunities

**Priority: P1**

The application exclusively uses targeted swaps (`hx-target="..."`) and body-level event triggers to coordinate multi-component updates. This means after a booking item action:

1. Controller returns `SwapResponse().WithTrigger("bookings.items.refresh").Build()`
2. The `#booking-items` div detects the trigger and re-fetches its content
3. The `#booking-payments` div also detects its trigger and re-fetches

This creates **N parallel HTTP requests** — one per listening component. After creating a booking item, up to 3–4 sections may independently re-fetch.

**Fix**: Use OOB (Out-of-Band) swaps for operations where the server already has the updated data:

```csharp
return SwapResponse()
    .WithView("_ModalClose")
    .WithOobView("_ItemsList", itemsModel, "#booking-items")
    .WithOobView("_PaymentSummary", paymentModel, "#booking-payments")
    .WithSuccessToast("Item created.")
    .Build();
```

This returns all updated partials in a single response, eliminating the cascade of re-fetch requests.

### High-Impact Candidates for OOB

| Action | Current Triggers | Fetches Caused | OOB Opportunity |
|--------|-----------------|----------------|-----------------|
| Create booking item | `items.refresh` | Items list + payment summary | Return both in 1 response |
| Confirm supplier | `items.refresh` | Items list re-fetch | Return items in response |
| Add payment | `payments.refresh` | Payments list re-fetch | Return payments in response |
| Update quote status | `details.refresh` + `refresh` | Summary + list re-fetch | Return summary in response |
| Create supplier contact | `contacts.refresh` | Contacts list re-fetch | Return contacts in response |

---

## HX-2. Booking Details Page Fires 9 Parallel Requests on Load

**Priority: P1**

The booking details page (`Details.cshtml`) has 9 independently HTMX-loaded sections, all with `hx-trigger="load"`:

1. `#booking-summary` (via dedicated `_Summary` partial with its own endpoint)
2. `#booking-items`
3. `#booking-payment-links`
4. `#booking-payments`
5. `#booking-documents`
6. `#booking-assignments`
7. `#booking-comments`
8. `#booking-activity`
9. `#booking-communications`

On initial page load, the browser fires **9 parallel GET requests** to the server. Each hits the database independently.

**Fix — Phased loading**: Not all sections need to load immediately.

- **Phase 1 (immediate)**: Summary + Items (core booking data, server-render in the initial response instead of HTMX lazy load)
- **Phase 2 (on load)**: Payments, Payment Links, Documents
- **Phase 3 (deferred, `hx-trigger="revealed"`)**: Comments, Activity, Communications, Assignments

Using `hx-trigger="revealed"` means these sections only load when scrolled into view, saving 4 requests on initial load.

Additionally, the Summary section could be **server-rendered inline** in the details page response (it's always needed), avoiding 1 more request.

---

## HX-3. Quote Builder Preview Fires on Every Keystroke

**Priority: P2**

The quote builder's live preview uses:
```html
hx-trigger="load, change from:#quote-builder-form, input delay:500ms"
```

This means every form input triggers a preview re-render after 500ms delay. While the delay helps, rapid typing in the Notes field (2000+ chars) still causes many server round-trips.

**Fix**: Increase the delay for text inputs to 1000ms, and limit preview triggers to fields that actually affect the preview (rate card selection, currency, markup, template layout). Notes and internal notes don't need to trigger preview refreshes.

```html
hx-trigger="load, change from:.preview-trigger, input from:.preview-trigger delay:1000ms"
```

Add `class="preview-trigger"` only to fields that affect visual output.

---

## HX-4. Modal Container Not Cleaned Up Reliably

**Priority: P2**

When a modal is closed via the backdrop click (`onclick="this.closest('.modal').classList.remove('modal-open')"`), the modal HTML remains in `#modal-container`. This means:

1. Screen readers may still see the old modal content.
2. If a new modal opens, the old content briefly flashes before being replaced.
3. Keyboard focus may get trapped in the invisible modal DOM.

**Fix**: Add a global HTMX event handler that empties `#modal-container` when `modal-open` is removed:

```javascript
document.addEventListener('htmx:afterSwap', function(evt) {
    if (evt.detail.target.id === 'modal-container') {
        var modal = evt.detail.target.querySelector('.modal');
        if (!modal) {
            evt.detail.target.innerHTML = '';
            evt.detail.target.setAttribute('aria-hidden', 'true');
        }
    }
});
```

Also clear the container when Escape is pressed (the existing Escape handler removes `modal-open` but doesn't clear the DOM).

---

## HX-5. Loading Button State Not Reset on Error

**Priority: P2**

`layout.js` adds `loading` + `loading-spinner` classes to buttons during HTMX requests:
```javascript
document.addEventListener('htmx:beforeRequest', function(evt) {
    var trigger = evt.detail.elt;
    if (trigger?.tagName === 'BUTTON') {
        trigger.disabled = true;
        trigger.classList.add('loading', 'loading-spinner');
    }
});
```

But the reset handler in `htmx:afterRequest` may not fire for network errors or timeouts. The button stays in loading state permanently.

**Fix**: Also listen for `htmx:responseError`, `htmx:sendError`, and `htmx:timeout` to reset button state:

```javascript
['htmx:responseError', 'htmx:sendError', 'htmx:timeout'].forEach(function(eventName) {
    document.addEventListener(eventName, function(evt) {
        var trigger = evt.detail.elt;
        if (trigger?.tagName === 'BUTTON') {
            trigger.disabled = false;
            trigger.classList.remove('loading', 'loading-spinner');
        }
    });
});
```

---

## HX-6. Tab Navigation in Bookings/Quotes/Inventory Uses Full Page Reload

**Priority: P2**

Status tabs (e.g., All / Provisional / Confirmed / Completed in Bookings) render as `<a href="...">` elements that trigger a full page navigation:

```html
<a href="@TabUrl(nameof(BookingStatus.Provisional))">Provisional</a>
```

Because these are plain links processed by Swap.Htmx boost, they replace `#main-content`. But the tab state is URL-driven, meaning switching tabs causes a full page content swap including the search form, tab bar, and list.

**Fix**: Change tabs to use `hx-get` targeting only `#booking-list` (or equivalent list container), preserving the search form and tab bar:

```html
<a hx-get="@Url.Action("List", new { slug, status = "Provisional" })"
   hx-target="#booking-list"
   hx-push-url="@TabUrl("Provisional")"
   class="tab @tabClass("Provisional")">
    Provisional
</a>
```

This makes tab switching feel instant and preserves any in-progress search text.

---

## HX-7. Toast Auto-Dismiss Doesn't Account for User Interaction

**Priority: P3**

Toasts auto-dismiss after 5 seconds via `setTimeout`. If the user is reading a long error message, it disappears before they can act on it.

**Fix**: Pause the auto-dismiss timer when the user hovers over the toast. Add a manual dismiss (×) button as well:

```javascript
node.addEventListener('mouseenter', function() { clearTimeout(node._timeout); });
node.addEventListener('mouseleave', function() {
    node._timeout = setTimeout(function() { /* fade and remove */ }, 3000);
});
```

---

## HX-8. Rate Card Grid Inline Save Targets Toast Only

**Priority: P2**

Rate card rate cells use inline forms that POST to `UpdateRate` with `hx-target="#toast-container" hx-swap="beforeend"`. This means:
1. After saving a rate, the grid cell doesn't visually confirm the save.
2. The toast appears far from the cell being edited.
3. No feedback if the save silently fails.

**Fix**: Add an `hx-indicator` on the cell (a tiny spinner), and use OOB to return both the toast and a confirmation class on the saved cell:

```html
<form hx-post="..." hx-target="this" hx-swap="outerHTML">
```

The response replaces the form with an updated version that briefly flashes a success indicator.

---

## HX-9. Search Inputs Don't Show Loading Indicator

**Priority: P2**

All search inputs use `hx-trigger="keyup changed delay:300ms"` but don't show any loading indicator while the search request is in flight. Users don't know if the app is processing their query.

**Fix**: Add `hx-indicator` pointing to a small spinner next to the search input:

```html
<span class="loading loading-spinner loading-sm htmx-indicator" id="search-spinner"></span>
<input ... hx-indicator="#search-spinner" />
```
