# Client Experience — Feature Specifications

Four features that create a branded, self-service experience for end-clients of the travel agency.

---

## Client Portal

### Overview

A public-facing, branded portal where travel agency clients can view their bookings, quotes, itineraries, documents, and make payments. Access is via secure, time-limited token URLs — no client account registration required. Each agency's portal is styled with their Branding settings.

### Approach

New top-level module: `Portal`. Completely separate from the tenant admin UI. The portal uses token-based authentication (not ASP.NET Identity). Agencies share URLs with clients that contain a secure token. Tokens are scoped to a client and tenant, with configurable expiry.

### Entities

```
TenantDbContext:
  PortalSession         → authenticated client session via token
  PortalLink            → shareable link created by the agency for a client
```

**PortalLink**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| ClientId | Guid | FK → Client |
| Token | string(128) | Unique, cryptographically random |
| ExpiresAt | DateTime | When the link stops working |
| Scope | PortalLinkScope | Full, BookingOnly, QuoteOnly |
| ScopedEntityId | Guid? | If BookingOnly/QuoteOnly, the specific entity |
| CreatedByUserId | string(450) | Who created the link |
| CreatedAt | DateTime | |
| LastAccessedAt | DateTime? | |
| IsRevoked | bool | Manual revocation |

**PortalSession**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| PortalLinkId | Guid | FK → PortalLink |
| ClientId | Guid | FK → Client |
| StartedAt | DateTime | Session start |
| LastActivityAt | DateTime | Updated on each request |
| IpAddress | string(45) | Client IP |

**Enums:**
- `PortalLinkScope`: Full = 0, BookingOnly = 1, QuoteOnly = 2

### Module Registration

```csharp
public class PortalModule : IModule
{
    public string Name => "Portal";
    public IReadOnlyList<ModuleFeature> Features =>
    [
        new("portal", "Client Portal", MinPlanSlug: "professional")
    ];
    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new("portal.manage", "Manage Portal Links", "Portal", 0),
    ];
}
```

### URL Routes

**Admin routes (authenticated):**

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/portal/links` | Manage portal links |
| GET | `/{slug}/portal/links/new/{clientId}` | Create link form |
| POST | `/{slug}/portal/links/create` | Create portal link |
| POST | `/{slug}/portal/links/revoke/{id}` | Revoke a link |

**Public portal routes (token-authenticated):**

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/portal/{token}` | Portal entry — validates token, creates session, redirects |
| GET | `/portal/{token}/dashboard` | Client dashboard — bookings, quotes overview |
| GET | `/portal/{token}/bookings` | All bookings for this client |
| GET | `/portal/{token}/bookings/{bookingId}` | Booking detail view |
| GET | `/portal/{token}/quotes` | All quotes for this client |
| GET | `/portal/{token}/quotes/{quoteId}` | Quote detail (PDF-style) |
| GET | `/portal/{token}/itinerary/{itineraryId}` | Itinerary view |
| GET | `/portal/{token}/documents` | Documents list |
| GET | `/portal/{token}/documents/download/{docId}` | Download a document |

### UI/UX Design

**Branding Integration:**
- Portal pages use the agency's Branding settings: logo, colors, company name
- Separate layout (`_PortalLayout.cshtml`) distinct from the admin/tenant layout
- Clean, minimal styling focused on readability for end-clients

**Dashboard:**
- Welcome message with agency branding
- Summary cards: upcoming trips, pending quotes, documents
- Quick links to recent bookings

**Booking Detail (public view):**
- Booking reference, dates, destination
- Itinerary summary (if itinerary is linked)
- List of services/items (hotel, transfers) — no cost data shown unless agency opts in
- Status indicators (Confirmed, Pending, etc.)

**Quote View:**
- Styled like the existing quote PDF but interactive
- Accept/Decline buttons (see Self-Service Booking spec below)

**Security Model:**
- Tokens are 128-character cryptographically random strings (using `RandomNumberGenerator`)
- Configurable default expiry (7/14/30/90 days) in Settings
- Automatic session timeout after 30 minutes of inactivity
- Portal middleware validates token on every request, checks expiry and revocation
- No PII is stored in the token itself — all data fetched server-side via the token lookup

### Dependencies

- **Branding** — portal styling
- **Clients** — client data
- **Bookings** — booking display
- **Quotes** — quote display
- **Itineraries** (TRAVEL-OPERATIONS) — itinerary display
- **Documents** (TRAVEL-OPERATIONS) — document access

---

## Online Payments

### Overview

Payment links that clients can use to pay for bookings through the portal. Agencies create payment links with specific amounts that are sent to clients. Clients click through to a payment page and complete payment via Stripe.

### Approach

Build on top of the existing Billing module's Stripe integration. Payment links are separate from subscription billing — they use Stripe Checkout Sessions in "payment" mode (not "subscription"). The agency's Stripe Connect account receives the funds.

### Entities

```
TenantDbContext:
  PaymentLink           → a payment request sent to a client
```

**PaymentLink**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| BookingId | Guid | FK → Booking |
| ClientId | Guid | FK → Client |
| Amount | decimal(18,2) | Payment amount |
| CurrencyCode | string(3) | ISO currency code |
| Token | string(128) | Unique, cryptographically random |
| Status | PaymentLinkStatus | Pending, Paid, Expired, Cancelled |
| Description | string(500) | What the payment is for (shown to client) |
| StripeSessionId | string(200)? | Stripe Checkout Session ID |
| PaidAt | DateTime? | When payment completed |
| ExpiresAt | DateTime | Link expiry |
| CreatedByUserId | string(450) | |
| CreatedAt | DateTime | |

**Enums:**
- `PaymentLinkStatus`: Pending = 0, Paid = 1, Expired = 2, Cancelled = 3

### Module Integration

Part of the Billing module — extends existing Stripe infrastructure.

### URL Routes

**Admin routes:**

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/bookings/payments/{bookingId}` | Payments tab on booking detail |
| GET | `/{slug}/bookings/payments/new/{bookingId}` | Create payment link form |
| POST | `/{slug}/bookings/payments/create/{bookingId}` | Create payment link |
| POST | `/{slug}/bookings/payments/cancel/{id}` | Cancel payment link |
| GET | `/{slug}/bookings/payments/resend/{id}` | Resend payment link email |

**Public payment routes:**

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/pay/{token}` | Payment landing page (amount, description, pay button) |
| POST | `/pay/{token}/checkout` | Creates Stripe Checkout Session, redirects |
| GET | `/pay/{token}/success` | Post-payment success page |
| GET | `/pay/{token}/cancel` | Payment cancelled page |
| POST | `/pay/webhook` | Stripe webhook for payment completion |

### UI/UX Design

**Booking Detail — Payments Tab:**
- List of payment links for this booking: amount, status, created date, link
- "Create Payment Link" button opens modal
- Copy link button for sharing via chat/email
- "Send via Email" button sends payment link email to client

**Payment Landing Page:**
- Agency branded (using Branding settings)
- Shows: agency logo, booking reference, amount, currency, payment description
- "Pay Now" button → redirects to Stripe Checkout

**Success Page:**
- Confirmation with booking reference and payment amount
- "Return to portal" link if they accessed via portal

**Webhook Processing:**
- `checkout.session.completed` event updates PaymentLink status to Paid
- Records `PaidAt` timestamp
- If Payment Tracking (TRAVEL-OPERATIONS) is implemented, automatically creates a `BookingPayment` entry
- Sends confirmation email to client and notification to agency

### Dependencies

- **Billing** — Stripe integration (API keys, webhook infrastructure)
- **Bookings** — linked entity
- **Clients** — client data for emails
- **Branding** — payment page styling
- **Email** — payment confirmation emails
- **Payment Tracking** (TRAVEL-OPERATIONS) — optional: auto-record payment

---

## Self-Service Booking

### Overview

Allow clients to take actions through the portal: accept/decline quotes, request changes, approve itineraries, and submit feedback. These actions create records that the agency team can review and act on.

### Approach

Client actions are recorded as `ClientAction` entries that appear in the agency's workflow. Actions are not auto-executed — they create notifications for the agency team, who then take the appropriate action. The exception is quote acceptance, which can optionally auto-update quote status.

### Entities

```
TenantDbContext:
  ClientAction          → an action taken by a client through the portal
```

**ClientAction**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| PortalSessionId | Guid | FK → PortalSession |
| ClientId | Guid | FK → Client |
| ActionType | ClientActionType | AcceptQuote, DeclineQuote, RequestChange, ApproveItinerary, SubmitFeedback |
| EntityType | string(50) | "Quote", "Booking", "Itinerary" |
| EntityId | Guid | The entity acted upon |
| Notes | string(2000)? | Client message (e.g. change request details) |
| Status | ClientActionStatus | Pending, Acknowledged, Actioned, Dismissed |
| AcknowledgedByUserId | string(450)? | Team member who handled it |
| AcknowledgedAt | DateTime? | |
| CreatedAt | DateTime | |

**Enums:**
- `ClientActionType`: AcceptQuote = 0, DeclineQuote = 1, RequestChange = 2, ApproveItinerary = 3, SubmitFeedback = 4
- `ClientActionStatus`: Pending = 0, Acknowledged = 1, Actioned = 2, Dismissed = 3

### Module Integration

Part of the Portal module.

### URL Routes

**Portal routes (token-authenticated):**

| HTTP | Route | Purpose |
|------|-------|---------|
| POST | `/portal/{token}/quotes/{quoteId}/accept` | Accept a quote |
| POST | `/portal/{token}/quotes/{quoteId}/decline` | Decline a quote |
| GET | `/portal/{token}/quotes/{quoteId}/change` | Request change form |
| POST | `/portal/{token}/quotes/{quoteId}/change` | Submit change request |
| POST | `/portal/{token}/itinerary/{id}/approve` | Approve itinerary |
| GET | `/portal/{token}/feedback/{bookingId}` | Feedback form |
| POST | `/portal/{token}/feedback/{bookingId}` | Submit feedback |

**Admin routes:**

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/portal/actions` | Action inbox — all pending client actions |
| GET | `/{slug}/portal/actions/list` | Filtered action list partial |
| POST | `/{slug}/portal/actions/acknowledge/{id}` | Mark as acknowledged |
| POST | `/{slug}/portal/actions/dismiss/{id}` | Dismiss action |

### UI/UX Design

**Portal — Quote Accept/Decline:**
- Prominent "Accept" and "Decline" buttons on quote view
- Decline shows optional reason textarea
- After accepting: confirmation message, optional redirect to payment link if one exists

**Portal — Request Change:**
- Text area for describing the desired change
- Appears on both quote and booking views
- Agency receives notification with the change request details

**Portal — Approve Itinerary:**
- "Looks good — Approve" button on itinerary view
- Agency notified of approval

**Admin — Action Inbox:**
- List of pending client actions: client name, action type, entity reference, timestamp
- Click to view details and jump to the related entity
- Quick acknowledge/dismiss buttons
- Dashboard widget showing count of pending actions

### Dependencies

- **Client Portal** — parent feature
- **Quotes** — quote acceptance/decline
- **Itineraries** (TRAVEL-OPERATIONS) — itinerary approval
- **Notifications** — team notifications on client actions

---

## Communication Log

### Overview

Track all communications with clients and suppliers across all channels: email, phone, WhatsApp, in-person meetings. Every touchpoint is logged, creating a complete communication history per client and per booking.

### Approach

Mostly manual entry (agents log their calls and meetings). Email communications sent through the system are auto-logged. The communication log lives in the existing Bookings and Clients modules as a cross-cutting concern.

### Entities

```
TenantDbContext:
  CommunicationEntry    → a logged communication with a client or supplier
```

**CommunicationEntry**

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| ClientId | Guid? | FK → Client (nullable if supplier-only) |
| SupplierId | Guid? | FK → Supplier (nullable if client-only) |
| BookingId | Guid? | FK → Booking (optional context) |
| Channel | CommunicationChannel | Email, Phone, WhatsApp, InPerson, Other |
| Direction | CommunicationDirection | Inbound, Outbound |
| Subject | string(200)? | Optional subject line |
| Content | string(4000) | Notes/summary of the communication |
| OccurredAt | DateTime | When the communication happened |
| LoggedByUserId | string(450) | Who logged this entry |
| CreatedAt | DateTime | When the entry was created |

**Enums:**
- `CommunicationChannel`: Email = 0, Phone = 1, WhatsApp = 2, InPerson = 3, Other = 4
- `CommunicationDirection`: Inbound = 0, Outbound = 1

### URL Routes

| HTTP | Route | Purpose |
|------|-------|---------|
| GET | `/{slug}/clients/comms/{clientId}` | Client communication log partial |
| GET | `/{slug}/bookings/comms/{bookingId}` | Booking communication log partial |
| GET | `/{slug}/comms/new` | New communication entry form |
| POST | `/{slug}/comms/create` | Create communication entry |
| GET | `/{slug}/comms/edit/{id}` | Edit entry form |
| POST | `/{slug}/comms/update/{id}` | Update entry |
| POST | `/{slug}/comms/delete/{id}` | Delete entry |

### UI/UX Design

**Client Detail — Communications Tab:**
- Reverse-chronological list of all communications for this client
- Each entry: channel icon, direction arrow (↗ outbound / ↙ inbound), subject, content preview, date, logged by
- "Log Communication" button opens form modal

**Booking Detail — Communications Tab:**
- Same layout, filtered to communications linked to this booking
- "Log Communication" button pre-fills the client and booking

**Communication Form Modal:**
- Channel dropdown (with icons)
- Direction toggle (Inbound / Outbound)
- Subject (optional), content textarea
- Date/time picker (defaults to now)
- Client picker (auto-filled from context)
- Booking picker (optional, auto-filled from context)

**Auto-logging:**
- When the system sends an email (quote share, supplier request, voucher), auto-create a CommunicationEntry with Channel=Email, Direction=Outbound, content = email subject and recipient

### HTMX Interactions

- Communication log loads via `hx-trigger="load, comms.refresh from:body"`
- Form submit triggers `comms.refresh`
- Auto-logged entries appear immediately in the feed

### Dependencies

- **Clients** — client communication history
- **Bookings** — booking communication context
- **Suppliers** (TRAVEL-OPERATIONS) — supplier communication history
- **Email** — auto-logging outbound emails
