# 06 — Performance

## Current State

The application uses SQLite (per-tenant), EF Core with `AsNoTracking()` for reads, and projection to DTOs via `.Select()`. Most queries are well-structured. However, several patterns create unnecessary load.

---

## PF-1. Booking Details Causes 9+ Database Roundtrips on Load

**Priority: P1**

When a booking detail page loads, each HTMX section fires its own controller action, which calls its own service method, which runs its own database query:

| Section | Service Call | DB Queries |
|---------|-------------|------------|
| Summary | `GetDetailsAsync(id)` | 1 (with includes) |
| Items | `GetBookingItemsAsync(id)` | 1 |
| Payments | `GetBookingPaymentsAsync(id)` | 1 + aggregation |
| Payment Links | `GetPaymentLinksAsync(id)` | 1 |
| Documents | `GetDocumentsAsync(id)` | 1 |
| Assignments | `GetAssignmentsAsync(id)` | 1 + user name resolution |
| Comments | `GetCommentsAsync(id)` | 1 + user name resolution |
| Activity | `GetActivityAsync(id)` | 1 |
| Communications | `GetByBookingAsync(id)` | 1 + user name resolution |

That's ~12 distinct DB queries for loading one booking.

**Fix — Server-render the top sections**:

Instead of lazy-loading Summary and Items via HTMX, render them inline in the initial `Details.cshtml` response:
```csharp
public async Task<IActionResult> Details(Guid id)
{
    var details = await _service.GetDetailsAsync(id);  // Already includes items
    return SwapView(details);
}
```

This eliminates 2–3 requests. The remaining sections can load via HTMX with `hx-trigger="revealed"` (see HX-2).

---

## PF-2. User Name Resolution Repeated Across Services

**Priority: P2**

Multiple services independently resolve user IDs to display names:
- `CollaborationService.GetAssignmentsAsync()` — loads users dictionary
- `CollaborationService.GetCommentsAsync()` — loads users dictionary again
- `CommunicationService.MapToDtosAsync()` — loads users dictionary again
- `TaskService.GetListAsync()` — loads users separately

Each call runs `_db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(...)`.

**Fix**: Create a lightweight `IUserNameResolver` service that caches user names per-request using `IMemoryCache` or scoped lifetime:

```csharp
public interface IUserNameResolver
{
    Task<string> GetDisplayNameAsync(string userId);
    Task<Dictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds);
}
```

Register as scoped. First call loads from DB, subsequent calls in the same request hit cache.

---

## PF-3. Layout Feature Flag Checks on Every Request

**Priority: P2**

`_TenantLayout.cshtml` calls `FeatureService.IsEnabledAsync()` for **8 feature flags** on every page render:

```csharp
var brandingEnabled = await FeatureService.IsEnabledAsync("branding");
var bookingsEnabled = await FeatureService.IsEnabledAsync("bookings");
var clientsEnabled = await FeatureService.IsEnabledAsync("clients");
// ... 5 more
```

If `IsEnabledAsync` hits the database each time, that's 8 queries per page load just for sidebar rendering.

**Fix**: Feature flags should be cached per-tenant. Load all flags once at request start (middleware or tenant resolution) and serve from an in-memory dictionary for the rest of the request. If already cached, verify this — if each call is a DB hit, this is significant.

---

## PF-4. Quote Preview Deep Load Can Be Expensive

**Priority: P2**

`BuildPreviewAsync()` loads all selected rate cards with their full tree:
- RateCard → Seasons → Rates → RoomType
- RateCard → Seasons → Rates → RateCategory
- RateCard → InventoryItem → Destination
- All currencies for exchange calculation

For a quote with 10 rate cards, each with 4 seasons and 8 room types, that's ~320 rate cells loaded. The preview fires on every form input change (500ms debounce).

**Fix**:
1. Increase debounce to 1000ms for text fields (see HX-3).
2. Cache rate card data per-session — rate cards don't change while the user is building a quote. Only currency and markup need recalculation.
3. Consider a server-side preview cache keyed on selected rate card IDs + currency + markup.

---

## PF-5. Supplier Search Does Full Table Scan

**Priority: P2**

`SupplierService.GetListAsync()` searches with `.Where(s.Name.ToLower().Contains(term))`. SQLite's `LIKE` with a leading wildcard (`%term%`) can't use the Name index.

This is the same pattern used across all modules (Clients, Inventory, Bookings, etc.). For small datasets (<500 rows) this is fine. For larger ones, it becomes slow.

**Fix**: No immediate fix needed for current scale. But if any list grows beyond 1000 rows, consider:
- SQLite FTS5 virtual table for full-text search
- StartsWith matching (prefix search) which can use indexes
- Debouncing the search input to 500ms+ (already done in most places)

---

## PF-6. Rate Card Grid Loads All Seasons and Rates at Once

**Priority: P2**

`GetDetailsAsync()` uses deeply nested includes to load the entire rate card with all seasons, rates, room types, and rate categories in one query. For a rate card with 6 seasons × 20 room types = 120 rate cells, this is manageable. But some agencies may have 10+ seasons.

**Fix**: For rate cards with many seasons (>6), consider paginating the grid by season or loading seasons in batches. The grid partial could use tabs per season instead of one massive table.

---

## PF-7. Settings Seed Check on Every Tenant Access

**Priority: P3**

The Settings module's `SeedTenantAsync()` may run on every first request to a tenant to ensure default data exists. Verify this isn't creating unnecessary overhead.

**Fix**: Track seed completion in a tenant metadata flag to skip re-checking.

---

## PF-8. Communications Module Loads All Entries Without Limit

**Priority: P2**

`QueryEntries()` in `CommunicationService` returns all entries ordered by date with no `Take()` limit:

```csharp
private IQueryable<CommunicationEntry> QueryEntries()
{
    return _db.CommunicationEntries.OrderByDescending(e => e.OccurredAt);
}
```

For long-running client relationships, this could return hundreds of entries.

**Fix**: Default to `Take(20)` with a "Load more" button, or paginate.
