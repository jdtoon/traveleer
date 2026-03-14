# 05 — Cross-Module Linking

## Current State

Modules reference each other through foreign keys and dropdown selects, but **most cross-references are not clickable**. Users see entity names as plain text and must manually navigate to the related module to find more details.

### Current Cross-Reference Map

```
Bookings ←──→ Clients       (ClientId FK, ClientName displayed)
Bookings ←──→ Quotes        (QuoteId FK, bidirectional link on detail pages)
Bookings ───→ Suppliers      (via BookingItem.SupplierId, name displayed)
Bookings ───→ Inventory      (via BookingItem, ServiceName displayed)
Quotes   ───→ Clients        (ClientId FK, name displayed)
Quotes   ───→ RateCards      (QuoteRateCard junction, names displayed)
RateCards ──→ Inventory      (InventoryItemId FK, name/destination displayed)
RateCards ──→ Settings       (MealPlan, RoomType, RateCategory)
Inventory ──→ Suppliers      (SupplierId FK, name displayed)
Inventory ──→ Destinations   (DestinationId FK, name displayed)
Itineraries → Clients        (ClientId FK, name displayed)
Itineraries → Bookings       (BookingId FK, ref displayed)
Tasks     ──→ Any entity     (LinkedEntityType + LinkedEntityId, polymorphic)
Portal    ──→ Clients        (via PortalLink.ClientId)
Comms     ──→ Clients/Suppliers/Bookings (poly-context, all nullable FKs)
```

---

## CL-1. Client Name Not Clickable in Booking List and Detail

**Priority: P1**

In the booking list (`_List.cshtml`), the client name appears as plain text. Users can't click through to the client profile. Same in the booking summary (`_Summary.cshtml`).

**Fix**: Make client names clickable links that open the client details modal:

```html
<a href="#" class="link link-hover"
   hx-get="@Url.Action("Details", "Client", new { slug, id = Model.ClientId })"
   hx-target="#modal-container">
    @Model.ClientName
</a>
```

### All Locations Where Client Name Should Be Clickable

| View | Current | Fix |
|------|---------|-----|
| Booking `_List.cshtml` | Plain text | Link → Client details modal |
| Booking `_Summary.cshtml` | Plain text | Link → Client details modal |
| Quote `_List.cshtml` | Plain text | Link → Client details modal |
| Quote `_Summary.cshtml` | Plain text | Link → Client details modal |
| Itinerary `_List.cshtml` | Plain text | Link → Client details modal |
| Portal Link `_LinkList.cshtml` | Plain text | Link → Client details modal |
| Report Top Clients widget | Plain text | Link → Client list filtered |

---

## CL-2. Supplier Name Not Clickable in Booking Items

**Priority: P1**

Booking items show the supplier name as plain text. Users managing supplier confirmations need quick access to supplier details (contact info, bank details, payment terms).

**Fix**: Make supplier names link to the supplier detail page:

```html
<a href="@Url.Action("Details", "Supplier", new { slug, id = item.SupplierId })"
   class="link link-hover">
    @item.SupplierName
</a>
```

### Locations for Supplier Links

| View | Current | Fix |
|------|---------|-----|
| Booking `_ItemsList.cshtml` | Plain text | Link → Supplier detail page |
| Inventory `_List.cshtml` | Plain text | Link → Supplier detail page |
| Report Top Suppliers widget | Plain text | Link → Supplier detail page |

---

## CL-3. Inventory Item Not Linked from Rate Card

**Priority: P2**

Rate card summary shows the inventory item name and destination as read-only text. Users building rate cards may want to check the inventory item's details (address, base cost, images).

**Fix**: Make the inventory item name a link in `_Summary.cshtml`:

```html
<a class="link link-hover"
   hx-get="@Url.Action("Edit", "Inventory", new { slug, id = Model.InventoryItemId })"
   hx-target="#modal-container">
    @Model.InventoryItemName
</a>
```

---

## CL-4. Booking Reference Not Linked from Quote Detail

**Priority: P1**

Quote details show "View Booking" as a button when a booking was converted from the quote. This works. However, the reverse link (Booking → Quote) should also be prominent.

Currently in booking summary:
```html
@if (Model.QuoteId.HasValue)
{
    <a href="@Url.Action("Details", "Quote", new { slug, id = Model.QuoteId })">View Quote</a>
}
```

**Fix**: Ensure this link is visually consistent. Both directions should use the same badge+link pattern:

```html
<div class="badge badge-outline gap-1">
    <svg class="h-3 w-3">...</svg>
    <a href="..." class="link">QT-2026-0042</a>
</div>
```

---

## CL-5. Tasks Don't Link to Their Related Entity

**Priority: P1**

Tasks have `LinkedEntityType` and `LinkedEntityId` for polymorphic linking. The task list (`_TaskList.cshtml`) shows the entity type/ID but doesn't make them clickable.

**Fix**: Build a URL resolver that maps entity type → detail page:

```csharp
private string? GetEntityUrl(string? entityType, Guid? entityId)
{
    if (entityType is null || entityId is null) return null;
    return entityType switch
    {
        "Booking" => Url.Action("Details", "Booking", new { slug, id = entityId }),
        "Quote" => Url.Action("Details", "Quote", new { slug, id = entityId }),
        "Client" => Url.Action("Details", "Client", new { slug, id = entityId }),
        _ => null
    };
}
```

Render as a clickable badge in the task row:
```html
<a href="@entityUrl" class="badge badge-outline badge-sm">@entityType: @entityRef</a>
```

---

## CL-6. Client Detail Modal Missing Booking/Quote History

**Priority: P1**

The client details modal (`_Details.cshtml`) shows:
- Client profile info (name, company, email, phone, address, notes)
- Documents section (lazy-loaded via HTMX)
- Communications section (lazy-loaded via HTMX)

**Missing**: Recent bookings and quotes for this client. Users viewing a client want to see their history.

**Fix**: Add two more HTMX sections to the client details modal:

```html
<div class="divider my-2"></div>
<h4 class="font-semibold text-sm mb-2">Recent Bookings</h4>
<div id="client-bookings"
     hx-get="@Url.Action("ClientBookings", "Booking", new { slug, clientId = Model.Id })"
     hx-trigger="load"
     hx-swap="innerHTML">
    <span class="loading loading-spinner loading-sm"></span>
</div>

<div class="divider my-2"></div>
<h4 class="font-semibold text-sm mb-2">Recent Quotes</h4>
<div id="client-quotes"
     hx-get="@Url.Action("ClientQuotes", "Quote", new { slug, clientId = Model.Id })"
     hx-trigger="load"
     hx-swap="innerHTML">
    <span class="loading loading-spinner loading-sm"></span>
</div>
```

Requires adding `ClientBookings()` and `ClientQuotes()` controller actions that return compact list partials.

---

## CL-7. Supplier Detail Page Missing Rate Card List

**Priority: P2**

The supplier detail page shows business details, contacts, and booking item count. But it doesn't show which rate cards reference this supplier's inventory items.

**Fix**: Add a "Rate Cards" section to the supplier detail page that lists rate cards whose inventory item has this supplier:

```html
<div id="supplier-ratecards"
     hx-get="@Url.Action("SupplierRateCards", "RateCard", new { slug, supplierId = Model.Id })"
     hx-trigger="load"
     hx-swap="innerHTML">
</div>
```

---

## CL-8. Communications Module Doesn't Show Entity Context

**Priority: P2**

Communication entries are logged with polymorphic context (ClientId, SupplierId, BookingId). But the communication list view doesn't show which entities are linked — it only shows the channel, direction, subject, and content.

**Fix**: Show linked entity badges on each communication entry:

```html
@if (entry.BookingId.HasValue)
{
    <a href="..." class="badge badge-outline badge-xs">Booking</a>
}
@if (entry.SupplierId.HasValue)
{
    <span class="badge badge-outline badge-xs">Supplier</span>
}
```
