# Traveleer — Product Roadmap

Current state: 9 domain modules operational (Bookings, Quotes, Clients, Inventory, RateCards, Branding, Settings, Email, Onboarding) on top of a 12-module SaaS framework (Tenancy, Auth, Registration, Billing, SuperAdmin, FeatureFlags, Dashboard, TenantAdmin, Audit, Notifications, Marketing, Litestream).

This roadmap defines the next features, organized into three domains.

---

## Domain 1: Travel Operations

Deepen the operational toolkit agents use daily.

| Feature | Size | Priority | Dependencies | Spec |
|---------|------|----------|-------------- |------|
| Itineraries | XL | P1 | Bookings, Inventory, Branding | [TRAVEL-OPERATIONS.md](specs/TRAVEL-OPERATIONS.md#itineraries-module) |
| Supplier Management | L | P1 | Settings (Suppliers), Bookings | [TRAVEL-OPERATIONS.md](specs/TRAVEL-OPERATIONS.md#supplier-management-enhancement) |
| Document Management | M | P2 | Bookings, Clients, IStorageService | [TRAVEL-OPERATIONS.md](specs/TRAVEL-OPERATIONS.md#document-management) |
| Payment Tracking | L | P2 | Bookings, Clients, Billing | [TRAVEL-OPERATIONS.md](specs/TRAVEL-OPERATIONS.md#payment-tracking) |
| Transfer Management | S | P3 | Inventory | [TRAVEL-OPERATIONS.md](specs/TRAVEL-OPERATIONS.md#transfer-management) |

## Domain 2: Business Growth

Help agencies understand performance and work as a team.

| Feature | Size | Priority | Dependencies | Spec |
|---------|------|----------|-------------- |------|
| Reporting & Analytics | L | P1 | Bookings, Quotes, Clients | [BUSINESS-GROWTH.md](specs/BUSINESS-GROWTH.md#reporting--analytics-module) |
| Task Management | M | P2 | Bookings, Quotes, Clients | [BUSINESS-GROWTH.md](specs/BUSINESS-GROWTH.md#task-management) |
| Team Collaboration | M | P2 | Bookings, Auth | [BUSINESS-GROWTH.md](specs/BUSINESS-GROWTH.md#team-collaboration) |
| Audit Dashboard | S | P3 | Audit (framework) | [BUSINESS-GROWTH.md](specs/BUSINESS-GROWTH.md#audit-dashboard) |

## Domain 3: Client Experience

Give end clients visibility and self-service.

| Feature | Size | Priority | Dependencies | Spec |
|---------|------|----------|-------------- |------|
| Client Portal | XL | P1 | Quotes, Bookings, Branding | [CLIENT-EXPERIENCE.md](specs/CLIENT-EXPERIENCE.md#client-portal) |
| Online Payments | L | P2 | Client Portal, Billing | [CLIENT-EXPERIENCE.md](specs/CLIENT-EXPERIENCE.md#online-payments) |
| Self-Service Booking | M | P3 | Client Portal, Quotes | [CLIENT-EXPERIENCE.md](specs/CLIENT-EXPERIENCE.md#self-service-booking) |
| Communication Log | M | P2 | Clients, Bookings, Email | [CLIENT-EXPERIENCE.md](specs/CLIENT-EXPERIENCE.md#client-communication-log) |

---

## Sizing Key

| Size | Scope |
|------|-------|
| S | Single entity, one controller, < 5 views |
| M | 2–3 entities, service layer, 5–10 views, unit + integration tests |
| L | Full module with multiple entities, complex service logic, 10+ views, migrations |
| XL | Multi-module integration, PDF/document generation, external APIs, new public routes |

## Priority Key

| Priority | Meaning |
|----------|---------|
| P1 | Next wave — build after current stabilization is complete |
| P2 | Second wave — build after P1 features are live and stable |
| P3 | Third wave — build when P1+P2 are stable and demand is validated |

---

## Sequencing Constraints

1. **Supplier Management** should ship before or alongside **Itineraries** — itineraries need richer supplier data.
2. **Client Portal** depends on **Branding** being stable (already is) and benefits from **Itineraries** for display.
3. **Online Payments** requires **Client Portal** to exist.
4. **Self-Service Booking** requires both **Client Portal** and **Online Payments**.
5. **Reporting** can ship independently but becomes more valuable after **Payment Tracking** adds financial data.
6. **Task Management** and **Team Collaboration** are independent and can ship in either order.

## Suggested Build Order

```
Wave 1 (P1):
  Supplier Management → Itineraries → Reporting & Analytics

Wave 2 (P2):
  Payment Tracking → Document Management → Client Portal
  Task Management + Team Collaboration (parallel)
  Communication Log

Wave 3 (P3):
  Online Payments → Self-Service Booking
  Transfer Management
  Audit Dashboard
```
