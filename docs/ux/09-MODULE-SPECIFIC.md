# 09 — Module-Specific UX Issues

Detailed per-module issues not covered by the cross-cutting documents.

---

## Bookings Module

### BK-1. Booking Detail Page Is Very Long (P2)

The detail page has 9 sections in a single vertical column. On desktop, users scroll extensively to reach comments or activity.

**Fix**: Consider a two-column layout for the detail page on large screens:

```
┌─────────────────────────────┬─────────────────────┐
│ Summary                     │ Team / Assignments   │
├─────────────────────────────┤ Comments             │
│ Services / Items            │ Activity             │
├─────────────────────────────┤ Communications       │
│ Payments / Payment Links    │                      │
│ Documents                   │                      │
└─────────────────────────────┴─────────────────────┘
```

Or use a tabbed layout within the detail page for secondary sections (comments, activity, communications).

### BK-2. Supplier Status Actions Lack Confirmation (P2)

Actions like "Confirm Supplier", "Decline Supplier" on booking items execute immediately on button click. There's no confirmation step for potentially disruptive actions like "Decline".

**Fix**: Add a lightweight inline confirmation for destructive supplier status changes.

### BK-3. Booking List Doesn't Show Financial Summary (P2)

The booking list (`_List.cshtml`) shows reference, client, dates, status, and assignees. It doesn't show total selling price or profit margin. Users managing revenue need this at-a-glance.

**Fix**: Add `TotalSelling` and optionally `TotalCost` columns to the list view. These are already available in the booking entity.

### BK-4. No Booking Calendar View (P3)

Bookings have travel dates (`TravelStartDate`, `TravelEndDate`) but no calendar visualization. Users lose track of upcoming departures.

**Fix**: Add a calendar widget or timeline view that shows bookings on a date range. This could be a Reports-style widget or a dedicated view accessible from the Bookings index.

---

## Quotes Module

### QT-1. Quote Builder Is a Full Page, Not a Modal (P3)

Unlike most CRUD flows (which use modals), the quote builder is a full page (`SwapView("Builder")`). This is correct — the builder is complex with a live preview. No change needed, but the "Cancel" link should reliably navigate back to the quotes list.

### QT-2. Rate Card Selection Has No Search/Filter (P2)

The quote builder shows all active rate cards as a scrollable list of checkboxes. If a tenant has 50+ rate cards, finding the right one requires scrolling.

**Fix**: Add a search input above the rate card list that filters by hotel name or destination:

```html
<input type="text" placeholder="Filter rate cards..."
       oninput="filterRateCards(this.value)" class="input input-bordered input-sm w-full mb-2" />
```

### QT-3. Quote Version Comparison Not Possible (P3)

Users can view individual version snapshots but can't compare two versions side-by-side.

**Fix**: Low priority, but adding a "Compare with previous" view would help teams understand what changed between versions.

### QT-4. Sent Quote Has No Read Receipt (P3)

Email sending is tracked in `QuoteEmailLog` (sent/failed), but there's no indication whether the client opened or viewed the quote.

This is outside current scope but worth noting for future consideration.

---

## Clients Module

### CL-1. Client Details Is a Modal, Not a Page (P1)

Client details (`_Details.cshtml`) opens as a modal. For clients with many bookings, quotes, documents, and communications, the modal becomes very tall and scrolling is awkward.

**Fix**: Consider promoting client details to a full page (`SwapView`) similar to booking/supplier/itinerary details. The current modal pattern works for quick lookups, but complex client profiles need a dedicated page with proper section layout.

Alternatively, keep the modal for quick lookups but add a "View Full Profile" link that navigates to a dedicated page.

### CL-2. Client List Doesn't Show Booking/Quote Count (P2)

The client list shows name, company, contact, country, and created date. Users can't see at a glance which clients are active (have recent bookings/quotes).

**Fix**: Add `BookingCount` and `QuoteCount` columns (or a combined "Activity" column) to the list view.

### CL-3. No Client Import (P3)

Agencies migrating from spreadsheets need a way to import client lists. This is noted but not in scope for the UX improvement pass.

---

## Suppliers Module

### SP-1. Supplier List Clickable Rows Don't Work on Mobile (P2)

The supplier list uses `hx-get` on `<tr>` elements for row-click navigation. On mobile, this is unreliable because:
1. The dropdown menu button uses `stopPropagation` to prevent row navigation
2. Touch events may not fire `hx-get` consistently on `<tr>`
3. The entire row being clickable makes it hard to select text

**Fix**: Add an explicit "Open" button in the actions column (similar to Bookings/Quotes) instead of relying on row click only.

### SP-2. Supplier Delete Doesn't Check for References (P1)

`SupplierService.DeleteAsync()` removes the supplier and its contacts but doesn't check if booking items or inventory items reference this supplier. If items reference the deleted supplier, they'll have dangling `SupplierId` values.

**Fix**: Before deleting, check for references and warn the user:

```csharp
var referencedByBookings = await _db.BookingItems.AnyAsync(bi => bi.SupplierId == id);
var referencedByInventory = await _db.InventoryItems.AnyAsync(i => i.SupplierId == id);
if (referencedByBookings || referencedByInventory)
{
    return ("This supplier is referenced by existing bookings or inventory items.", false);
}
```

---

## Inventory Module

### INV-1. Inventory Image URL Has No Preview (P2)

The form has an Image URL field but no preview of the actual image. Users paste a URL and have no feedback about whether it's valid.

**Fix**: Add an `<img>` preview below the URL input that updates on blur/change:

```html
<img id="image-preview" src="" class="hidden rounded-lg max-h-32 mt-2"
     onerror="this.classList.add('hidden')"
     onload="this.classList.remove('hidden')" />
<input id="ImageUrl" name="ImageUrl"
       onblur="document.getElementById('image-preview').src = this.value" />
```

### INV-2. Transfer-Specific Fields Hidden by JavaScript Only (P2)

The `_Form.cshtml` uses inline JavaScript to toggle transfer-specific fields. This works but doesn't play well with server-side validation re-rendering (if the form reloads, the section may be hidden even though the selected type is Transfer).

**Fix**: Set the initial visibility based on the model's `Kind` value server-side:

```html
<div id="transfer-fields" class="@(Model.Kind != InventoryItemKind.Transfer ? "hidden" : "")">
```

---

## Rate Cards Module

### RC-1. Inline Rate Editing Has No Batch Save (P2)

Each rate cell in the grid is an independent form. Editing 20 rates requires 20 individual submissions. Users editing an entire season's pricing must click "Save" on each cell.

**Fix**: Add a "Save All Changes" button that submits all modified rates in a single POST. Track dirty cells client-side and batch them.

### RC-2. Grid Column Headers Don't Show Room Type Name (P3)

Columns show room type codes (SGL, DBL, TWN). Full names are not visible. Users unfamiliar with codes may be confused.

**Fix**: Add a tooltip or a smaller secondary line with the full name below the code:

```html
<th>
    <div>@roomType.Code</div>
    <div class="text-xs opacity-50 font-normal">@roomType.Name</div>
</th>
```

---

## Itineraries Module

### IT-1. Shared Itinerary Has No Branding (P2)

The `SharedView.cshtml` (public itinerary view at `/shared/itinerary/{slug}/{token}`) needs verification that it loads tenant branding (logo, colors, agency name).

**Fix**: Ensure the shared view pulls branding from `IBrandingService` and renders the agency logo and colors.

### IT-2. Day/Item Sort Order Not Draggable (P3)

Days and items have `SortOrder` fields but reordering requires editing each item's sort order manually.

**Fix**: Add drag-and-drop reordering using HTMX morphing or a lightweight JS library. The sort order can be saved via a batch endpoint.

---

## Tasks Module

### TK-1. Task Priority Colors Not Consistent (P2)

Verify that task priority badges (Low/Normal/High/Urgent) use consistent DaisyUI badge colors:

| Priority | Recommended Badge |
|----------|------------------|
| Low | `badge-ghost` |
| Normal | `badge-info` |
| High | `badge-warning` |
| Urgent | `badge-error` |

### TK-2. Task Due Date Not Relative (P2)

Due dates show as absolute dates (e.g., "Mar 18, 2026"). Users need relative context ("in 2 days", "overdue by 3 days").

**Fix**: Show relative date alongside absolute:

```html
<td class="@(isOverdue ? "text-error font-semibold" : "")">
    @task.DueDate?.ToString("dd MMM yyyy")
    @if (isOverdue)
    {
        <span class="text-xs">(overdue @daysOverdue days)</span>
    }
</td>
```

---

## Reports Module

### RP-1. No Charts — Data Displayed as Tables Only (P1)

The reports module has widget endpoints and DTOs for monthly revenue, status breakdowns, and pipeline data. But the views only render tables and numbers — no actual charts.

**Fix**: Add a lightweight charting library. Options:
- **Chart.js** via CDN — most popular, DaisyUI compatible
- **Alpine.js charts** — lighter
- Server-rendered SVG charts — no JS dependency

Revenue monthly, bookings by status, and quote pipeline are prime candidates for bar/pie charts.

### RP-2. No Custom Date Range (P2)

Reports only support preset ranges (This Month / Quarter / Year). Users may need "March 1–15" or "Last 90 days".

**Fix**: Add a date range picker with custom start/end dates. The service methods already accept `DateTime from, DateTime to` parameters.

### RP-3. Widget Drill-Down Not Possible (P2)

Clicking a "Top Client" should navigate to that client's detail page. Clicking a "Recent Booking" should open that booking. Currently, these are static text.

**Fix**: Make widget rows clickable with appropriate navigation (see 05-CROSS-MODULE-LINKING.md).

---

## Settings Module

### ST-1. Tab State Not Persisted in URL (P2)

Already covered in 01-NAVIGATION.md (N-6). Switching tabs loses state on page refresh.

### ST-2. No Search Within Settings Tabs (P2)

Settings tabs with many items (destinations, suppliers) have no search. Users must scroll to find a specific entry.

**Fix**: Add a search input per tab, similar to the pattern used in the main module list pages.

### ST-3. Sort Order Field Not User-Friendly (P3)

Entity sort order is a numeric field. Users must know what number to enter. No drag-and-drop reordering.

**Fix**: Either hide the sort order field and add drag-and-drop, or show existing items' sort orders alongside the input for context.

---

## Portal Module

### PT-1. Portal Dashboard Has No HTMX (P2)

The public portal dashboard (`Dashboard.cshtml`) server-renders everything. This is actually fine for a public-facing page (simpler, faster first paint). No change needed.

### PT-2. Portal Link Admin Has No Copy-to-Clipboard Feedback (P2)

The "Copy Link" button presumably copies the portal URL but should show a toast or brief inline confirmation that the copy succeeded.

### PT-3. No QR Code for Portal Links (P3)

Portal links are long hex tokens. Generating a QR code would make sharing easier in person or on printed documents.

---

## Branding Module

### BR-1. No Live Preview of Sidebar/Shell (P2)

The branding page lets users pick colors and see a small quote preview, but doesn't show how the sidebar will look with the new colors.

**Fix**: Add a mini sidebar preview swatch that live-updates as the user changes primary/secondary colors.

### BR-2. Logo URL Only — No File Upload (P2)

Agencies need to upload their logo, not paste a URL. Most users won't have their logo hosted on a public URL.

**Fix**: Add file upload support that stores the image and generates the URL automatically.
