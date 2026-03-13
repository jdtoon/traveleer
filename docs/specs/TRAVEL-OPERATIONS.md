# Travel Operations — Feature Specifications

Five features that deepen the operational toolkit for travel agents.

---

## Itineraries Module

### Overview

A day-by-day trip builder that lets agents compose a visual itinerary for their clients. Itineraries link to bookings and inventory, can be exported as branded PDFs, and shared with clients via the future Client Portal.

### Entities

```
TenantDbContext:
  Itinerary           → header (client, title, dates, status, branding overrides)
  ItineraryDay        → day within the itinerary (date, title, description, sort order)
  ItineraryItem       → item within a day (inventory link, description, times, images, sort order)
```

**Itinerary**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid? | Optional link to a booking |
| ClientId | Guid? | Optional link to a client |
| Title | string(200) | Required |
| Status | ItineraryStatus | Draft, Published, Archived |
| TravelStartDate | DateOnly? | First day of the trip |
| TravelEndDate | DateOnly? | Last day of the trip |
| CoverImageUrl | string(500)? | Hero image for PDF cover |
| Notes | string(2000)? | Internal notes |
| PublicNotes | string(2000)? | Client-facing notes |
| ShareToken | string(64)? | Token for public sharing URL |
| SharedAt | DateTime? | When the itinerary was shared |
| PublishedAt | DateTime? | When status moved to Published |

**ItineraryDay**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| ItineraryId | Guid | FK → Itinerary |
| DayNumber | int | 1-indexed day of the trip |
| Date | DateOnly? | Calculated from TravelStartDate + DayNumber |
| Title | string(200) | "Day 1 — Arrival in Cape Town" |
| Description | string(2000)? | Rich text day overview |
| SortOrder | int | Display order |

**ItineraryItem**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| ItineraryDayId | Guid | FK → ItineraryDay |
| InventoryItemId | Guid? | Optional link to inventory |
| BookingItemId | Guid? | Optional link to booking item |
| Title | string(200) | Display name (auto-filled from inventory if linked) |
| Description | string(2000)? | Details visible to client |
| StartTime | TimeOnly? | e.g. 09:00 for morning activity |
| EndTime | TimeOnly? | e.g. 12:00 |
| ImageUrl | string(500)? | Item-specific image |
| SortOrder | int | Display order within the day |
| ItemKind | InventoryItemKind? | Hotel, Activity, Transport, etc. |

### Module Registration

```csharp
public class ItinerariesModule : IModule
{
    public string Name => "Itineraries";
    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("itineraries", "Itineraries", MinPlanSlug: "starter")
    ];
    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("itineraries.read", "View Itineraries", "Itineraries", 0),
        new("itineraries.create", "Create Itineraries", "Itineraries", 1),
        new("itineraries.edit", "Edit Itineraries", "Itineraries", 2),
        new("itineraries.delete", "Delete Itineraries", "Itineraries", 3),
        new("itineraries.share", "Share Itineraries", "Itineraries", 4),
    ];
}
```

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/itineraries` | Index page |
| GET | `/{slug}/itineraries/list` | List partial |
| GET | `/{slug}/itineraries/new` | New form |
| POST | `/{slug}/itineraries/create` | Create |
| GET | `/{slug}/itineraries/details/{id}` | Detail page with day builder |
| POST | `/{slug}/itineraries/update/{id}` | Update header |
| POST | `/{slug}/itineraries/days/create/{itineraryId}` | Add day |
| POST | `/{slug}/itineraries/days/update/{dayId}` | Update day |
| POST | `/{slug}/itineraries/days/delete/{dayId}` | Remove day |
| POST | `/{slug}/itineraries/items/create/{dayId}` | Add item to day |
| POST | `/{slug}/itineraries/items/update/{itemId}` | Update item |
| POST | `/{slug}/itineraries/items/delete/{itemId}` | Remove item |
| POST | `/{slug}/itineraries/publish/{id}` | Set status → Published |
| POST | `/{slug}/itineraries/share/{id}` | Generate share token |
| GET | `/shared/itinerary/{token}` | Public view (unauthenticated) |
| GET | `/{slug}/itineraries/pdf/{id}` | Download branded PDF |

### UI/UX Design

**Index Page:** Table/card list of itineraries with status badges (Draft/Published/Archived), client name, travel dates, and action buttons.

**Detail Page — Day Builder:**
- Left panel: list of days with drag-to-reorder
- Main area: selected day's items, each showing time, title, image thumbnail, inventory link
- Right panel: itinerary header summary (client, dates, status)
- "Add Day" button appends a new day
- "Add Item" opens a modal with inventory search/select or freeform entry
- Items within a day support drag-to-reorder

**PDF Export:**
- Cover page with itinerary title, client name, travel dates, cover image, agency branding
- One page per day with items listed chronologically
- Each item: title, description, image, time slot
- Footer with agency contact info from BrandingSettings

**Empty State:** "No itineraries yet. Create your first itinerary to build a day-by-day trip plan for your clients."

### HTMX Interactions

- Day list refreshes via `itineraries.days.refresh` trigger
- Item list within a day refreshes via `itineraries.items.refresh` trigger
- Sort order updates use inline HTMX posts (no modal)
- Publishing triggers `itineraries.refresh` on the index list

### Dependencies

- **Bookings** — optional link from itinerary to booking
- **Inventory** — item references for auto-filling titles, images, descriptions
- **Branding** — PDF uses tenant branding (logo, colors, footer)
- **IStorageService** — cover image and item image uploads

### Testing Requirements

- Unit: Service logic for day numbering, date calculation, PDF generation
- Integration: Full page, partial isolation, CRUD flow with DB verification, PDF download, share token generation
- Browser QA: Day builder drag interactions, PDF visual quality, public shared view

---

## Supplier Management Enhancement

### Overview

Promote `Supplier` from a simple Settings reference entity to a full module. Suppliers are the backbone of a travel agency — hotels, airlines, ground handlers. This module adds contacts, contracts, commission tracking, payment terms, and performance ratings.

### Entities

The existing `Supplier` entity in Settings is retained and extended. New entities are added for contacts and contract terms.

**Supplier** (extended — stays in TenantDbContext via Settings)

| New Field | Type | Notes |
|-----------|------|-------|
| RegistrationNumber | string(100)? | Business registration / tax number |
| BankDetails | string(500)? | For payment processing |
| PaymentTerms | string(200)? | "Net 30", "Prepay", etc. |
| DefaultCommissionPercentage | decimal? | Base commission rate |
| DefaultCurrencyCode | string(10)? | Preferred invoicing currency |
| Rating | int? | 1–5 internal performance rating |
| Notes | string(2000)? | Internal notes |
| Website | string(500)? | Supplier website |
| Address | string(500)? | Physical address |

**SupplierContact** (new)

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| SupplierId | Guid | FK → Supplier |
| Name | string(150) | Contact person name |
| Role | string(100)? | "Reservations Manager", "Accounts" |
| Email | string(320)? | |
| Phone | string(50)? | |
| IsPrimary | bool | Highlighted in booking workflows |

### Module Registration

```csharp
public class SuppliersModule : IModule
{
    public string Name => "Suppliers";
    // Extends the existing Settings Suppliers section into a full module
    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("suppliers", "Supplier Management", MinPlanSlug: "starter")
    ];
    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("suppliers.read", "View Suppliers", "Suppliers", 0),
        new("suppliers.create", "Create Suppliers", "Suppliers", 1),
        new("suppliers.edit", "Edit Suppliers", "Suppliers", 2),
        new("suppliers.delete", "Delete Suppliers", "Suppliers", 3),
    ];
}
```

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/suppliers` | Index page |
| GET | `/{slug}/suppliers/list` | List partial |
| GET | `/{slug}/suppliers/new` | New form |
| POST | `/{slug}/suppliers/create` | Create |
| GET | `/{slug}/suppliers/details/{id}` | Detail page with contacts, bookings, rate cards |
| POST | `/{slug}/suppliers/update/{id}` | Update |
| POST | `/{slug}/suppliers/contacts/create/{supplierId}` | Add contact |
| POST | `/{slug}/suppliers/contacts/update/{contactId}` | Update contact |
| POST | `/{slug}/suppliers/contacts/delete/{contactId}` | Remove contact |

### UI/UX Design

**Index Page:** Searchable table with supplier name, primary contact, rating stars, active/inactive badge.

**Detail Page:**
- Header: supplier name, rating, status
- Tab 1 (Contacts): List of contacts with role, email, phone, primary flag
- Tab 2 (Bookings): Recent booking items linked to this supplier
- Tab 3 (Rate Cards): Active rate cards for this supplier
- Tab 4 (Details): Full details form (payment terms, bank, commission, notes)

### Dependencies

- **Settings** — existing Supplier entity is the base
- **Bookings** — booking items reference suppliers
- **RateCards** — rate cards link to suppliers via inventory items

---

## Document Management

### Overview

Centralized document storage for bookings and clients. Agents upload vouchers, invoices, passports, visas, insurance documents, and any other file. Documents are stored via `IStorageService` and displayed in context on booking and client detail pages.

### Entities

```
TenantDbContext:
  Document            → file metadata (name, type, size, storage key, linked entity)
```

**Document**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid? | FK → Booking (nullable) |
| ClientId | Guid? | FK → Client (nullable) |
| FileName | string(255) | Original file name |
| ContentType | string(100) | MIME type |
| FileSize | long | Bytes |
| StorageKey | string(500) | Key for IStorageService |
| DocumentType | DocumentType | Voucher, Invoice, Passport, Visa, Insurance, Other |
| Description | string(500)? | Optional notes |
| UploadedBy | string(200)? | User who uploaded |

### Module Registration

Part of Bookings module (not a separate module). Documents are a supporting feature within bookings and clients, not a standalone workflow.

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| POST | `/{slug}/bookings/documents/upload/{bookingId}` | Upload to booking |
| POST | `/{slug}/clients/documents/upload/{clientId}` | Upload to client |
| GET | `/{slug}/documents/download/{id}` | Download file |
| POST | `/{slug}/documents/delete/{id}` | Remove document |
| GET | `/{slug}/bookings/documents/{bookingId}` | Document list partial for booking |
| GET | `/{slug}/clients/documents/{clientId}` | Document list partial for client |

### UI/UX Design

- Document list appears as a tab on booking detail and client detail pages
- Upload via drag-and-drop zone or file picker
- Each document shows icon (by type), filename, size, upload date, actions (download/delete)
- File preview for images and PDFs via modal
- DocumentType shown as badges

### Security

- Files are stored via `IStorageService` (local or S3-compatible)
- File type validation — reject executables and scripts
- File size limit — configurable, default 10 MB
- Documents are tenant-scoped — no cross-tenant access

### Dependencies

- **Bookings** — document list tab on booking details
- **Clients** — document list tab on client details
- **IStorageService** — file storage backend

---

## Payment Tracking

### Overview

Track client payments received and supplier payments made for each booking. Shows payment balance, outstanding amounts, and payment history. Enables agents to know exactly what money is owed and by whom.

### Entities

```
TenantDbContext:
  BookingPayment      → payment received from or refunded to the client
  SupplierPayment     → payment made to or received from a supplier
```

**BookingPayment**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid | FK → Booking |
| Amount | decimal | Payment amount |
| CurrencyCode | string(10) | |
| PaymentDate | DateOnly | When payment was received |
| PaymentMethod | PaymentMethod | Cash, BankTransfer, CreditCard, Online, Other |
| Reference | string(100)? | Transaction reference |
| Direction | PaymentDirection | Received (from client), Refunded (to client) |
| Notes | string(500)? | |

**SupplierPayment**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingItemId | Guid | FK → BookingItem |
| SupplierId | Guid | FK → Supplier |
| Amount | decimal | |
| CurrencyCode | string(10) | |
| PaymentDate | DateOnly | |
| PaymentMethod | PaymentMethod | |
| Reference | string(100)? | |
| Direction | PaymentDirection | Paid (to supplier), Received (from supplier — refund/credit) |
| Notes | string(500)? | |

**Enums:**
- `PaymentMethod`: Cash, BankTransfer, CreditCard, Online, Other
- `PaymentDirection`: Received, Refunded (client); Paid, Received (supplier)

### Module Registration

Part of Bookings module. Payments are a sub-feature of bookings — they appear as tabs on booking detail pages.

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/bookings/payments/{bookingId}` | Payment summary partial |
| GET | `/{slug}/bookings/payments/new/{bookingId}` | New client payment form |
| POST | `/{slug}/bookings/payments/create/{bookingId}` | Create client payment |
| POST | `/{slug}/bookings/payments/delete/{paymentId}` | Remove payment |
| GET | `/{slug}/bookings/items/payments/{bookingItemId}` | Supplier payment list |
| GET | `/{slug}/bookings/items/payments/new/{bookingItemId}` | New supplier payment form |
| POST | `/{slug}/bookings/items/payments/create/{bookingItemId}` | Create supplier payment |

### UI/UX Design

**Booking Detail — Payments Tab:**
- Summary card: Total selling, Total received, Outstanding balance
- Color-coded: green (fully paid), yellow (partially paid), red (overdue if past travel date)
- Table of client payments: date, amount, method, reference, notes
- "Record Payment" button opens modal

**Booking Item — Supplier Payment:**
- Each booking item shows: cost price, amount paid to supplier, outstanding
- "Record Payment" button per item

### Computed Fields on Booking

| Field | Derivation |
|-------|------------|
| TotalReceived | Sum of BookingPayments where Direction = Received minus Direction = Refunded |
| TotalPaidToSuppliers | Sum of SupplierPayments where Direction = Paid minus Direction = Received |
| ClientBalance | TotalSelling - TotalReceived |
| SupplierBalance | TotalCost - TotalPaidToSuppliers |

### Dependencies

- **Bookings** — parent entity for payments
- **Settings** — currencies for payment amounts

---

## Transfer Management

### Overview

Currently, transfers (airport pickups, inter-city transport, meet-and-greet) are stored as flat `InventoryItem` records with `Kind = Transport`. This enhancement adds structured fields for pickup/dropoff locations, vehicle type, and capacity without creating a separate entity.

### Approach

Extend `InventoryItem` with optional transport-specific fields:

| New Field | Type | Notes |
|-----------|------|-------|
| PickupLocation | string(200)? | e.g. "OR Tambo International Airport" |
| DropoffLocation | string(200)? | e.g. "Sandton City Hotel" |
| VehicleType | string(100)? | "Sedan", "Minibus", "Luxury SUV" |
| MaxPassengers | int? | Vehicle capacity |
| IncludesMeetAndGreet | bool | Default false |
| TransferDurationMinutes | int? | Estimated travel time |

These fields are only shown/editable when `Kind == Transport`.

### UI Changes

- Inventory create/edit form: conditionally show transport fields when Kind dropdown is set to "Transport"
- Inventory list: show pickup → dropoff route for transport items
- Booking item detail: display transfer info when the linked inventory is a transport

### Migration

- Add columns to existing `InventoryItems` table (all nullable, no data migration needed)
- Existing transport items continue to work — new fields are optional enhancements

### Dependencies

- **Inventory** — extends existing entity
- **Bookings** — display enrichment on booking items
