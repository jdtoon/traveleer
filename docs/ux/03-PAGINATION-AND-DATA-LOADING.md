# 03 — Pagination and Data Loading

## Current State

The application has a shared `PaginatedList<T>` implementation and a `_Pagination.cshtml` partial. However, pagination is inconsistently applied across modules.

### Pagination Audit

| Module | List Endpoint | Has Pagination | Default Page Size | Risk |
|--------|--------------|----------------|-------------------|------|
| **Bookings** | `/bookings/list` | **Yes** | 12 (range 6–48) | Low |
| **Clients** | `/clients/list` | **Yes** | 10 (range 5–50) | Low |
| **Quotes** | `/quotes/list` | **Yes** | 12 (range 6–48) | Low |
| **Inventory** | `/inventory/list` | **Yes** | 12 (range 6–48) | Low |
| **Rate Cards** | `/rate-cards/list` | **Yes** | 12 (range 6–48) | Low |
| **Suppliers** | `/suppliers/list` | **No** | ALL rows | **High** |
| **Itineraries** | `/itineraries/list` | **No** | ALL rows | **High** |
| **Tasks** | `/tasks/list` | **No** | ALL rows | **High** |
| **Settings** (per tab) | `/settings/room-types` etc. | **No** | ALL rows | Medium |
| **Communications** | `/comms/client/{id}` | **No** | ALL rows | Medium |
| **Portal Links** | `/portal/links` | **No** | ALL rows | Medium |
| **Audit Log** | `/audit` | Needs check | Unknown | Medium |
| **Reports** | Widget-based | N/A (Top 10) | Fixed | Low |

---

## PD-1. Suppliers List Has No Pagination

**Priority: P1**

`SupplierService.GetListAsync()` returns `List<SupplierListItemDto>` — all suppliers in one query. Travel agencies can have 200+ suppliers.

**Fix**: Change return type to `PaginatedList<SupplierListItemDto>`, add `page` and `pageSize` params. Update controller `List()` action and `_List.cshtml` to include `_Pagination` partial.

```csharp
// Before
public async Task<List<SupplierListItemDto>> GetListAsync(string? search = null)

// After
public async Task<PaginatedList<SupplierListItemDto>> GetListAsync(string? search = null, int page = 1, int pageSize = 12)
```

---

## PD-2. Itineraries List Has No Pagination

**Priority: P1**

`ItineraryService.GetListAsync()` returns all itineraries sorted by `CreatedAt DESC`. Active agencies building 5–10 itineraries per week will quickly accumulate hundreds.

**Fix**: Same approach — `PaginatedList<T>`, page/pageSize params, `_Pagination` partial in `_List.cshtml`.

---

## PD-3. Tasks List Has No Pagination

**Priority: P1**

`TaskService.GetListAsync()` returns all tasks matching the filter. Tasks accumulate over time and are rarely deleted. A busy agency can have 500+ tasks within months.

**Fix**: Add pagination. Consider defaulting to showing only `Open` and `InProgress` tasks, with a "Show completed" toggle that loads additional pages.

---

## PD-4. Settings Entity Lists Have No Pagination

**Priority: P2**

Settings tabs (Room Types, Meal Plans, Currencies, Destinations, Suppliers, Rate Categories) each load all entities. Most are small (<50 items), but Destinations and Suppliers can grow.

**Fix**: Add pagination to Destinations and Suppliers tabs. Other tabs can remain unpaginated if their cardinality is naturally low (<100).

---

## PD-5. Communications Entries Not Paginated

**Priority: P2**

`CommunicationService.GetByClientAsync()` and similar methods return ALL communication entries ordered by date. A client with years of interaction history could have hundreds of entries.

**Fix**: Add pagination or implement "load more" with a `Take(20)` initial load and a "Show older" button that fetches the next batch.

---

## PD-6. Inconsistent Default Page Sizes

**Priority: P2**

| Module | Default | Min | Max |
|--------|---------|-----|-----|
| Bookings | 12 | 6 | 48 |
| Clients | 10 | 5 | 50 |
| Quotes | 12 | 6 | 48 |
| Inventory | 12 | 6 | 48 |
| Rate Cards | 12 | 6 | 48 |

Clients uses different defaults (10/5/50) vs. everyone else (12/6/48).

**Fix**: Standardize to `12/6/48` across all modules for consistency.

---

## PD-7. Pagination Doesn't Preserve Filter State

**Priority: P1**

The `_Pagination.cshtml` partial builds URLs by appending `?page=N` to `Model.ListUrl`. But `ListUrl` must already contain the current search/filter params. This works when the calling view constructs the URL correctly, but it's fragile.

**Affected modules**: Any module where the list URL includes `search`, `status`, `type`, or other filters. If filters aren't encoded in `ListUrl`, clicking "Next" resets the filter.

**Fix**: Audit each module's `_List.cshtml` to ensure `listUrl` includes all active filter params. Consider passing filters as a dictionary to `ToPagination()`.

---

## PD-8. No Page Size Selector

**Priority: P3**

Users can't choose how many items per page. Power users managing large inventories may want 48 items; casual users may prefer 12.

**Fix**: Add a small dropdown next to pagination: "Show 12 | 24 | 48 per page". The selected size should be preserved in the URL params.

---

## PD-9. Portal Public Pages Have No Pagination

**Priority: P2**

Public portal pages (`/portal/{slug}/{token}/bookings`) load all client bookings/quotes/documents. A client with 50+ bookings would see a very long page.

**Fix**: Add server-side pagination to portal list pages. Use simplified pagination controls appropriate for the public portal theme.
