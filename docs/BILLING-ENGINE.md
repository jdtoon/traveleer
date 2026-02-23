# Billing Engine — Architecture & Implementation Plan

> **Status**: Complete — All 7 Phases ✅  
> **Date**: 2026-02-22  
> **Payment Gateway**: Paystack (exclusive)  
> **Currency**: ZAR (South African Rand)  
> **Tax**: 15% VAT (configurable)
>
> ### Progress Tracker
> | Phase | Status | Notes |
> |-------|--------|-------|
> | 1. Foundation (Entities + Config) | ✅ Done | All 14 entities, EF configs, migration, IBillingService (17 methods), MockBillingService, BillingOptions |
> | 2. Core Services | ✅ Done | CreditService, DiscountService, InvoiceEngine, SeatBillingService, UsageBillingService, DunningService, AddOnService — all registered in DI |
> | 3. Paystack Integration | ✅ Done | PaystackClient 6 new endpoints, PaystackBillingService full overhaul (5 stubs replaced), webhook idempotency, authorization storage, annual sync |
> | 4. Background Services | ✅ Done | DunningJob (hourly), UsageBillingJob (daily 1AM), DiscountExpiryJob (daily 4AM) — all registered in Hangfire |
> | 5. Tenant Portal | ✅ Done | Seat management, add-on subscriptions, discount codes, invoice detail, enhanced billing content |
> | 6. Super Admin | ✅ Done | Discount CRUD, Add-on CRUD, tenant billing detail, credit/refund ops, webhook event viewer, nav links |
> | 7. Testing & Polish | ✅ Done | 60 new tests across 6 files: CreditService (10), DiscountService (13), DunningService (7), AddOnService (11), SeatBillingService (9), InvoiceEngine (10) — 272 total tests passing |

---

## Table of Contents

1. [Overview](#1-overview)
2. [Current State Analysis](#2-current-state-analysis)
3. [Paystack API Capabilities](#3-paystack-api-capabilities)
4. [Entity Model](#4-entity-model)
5. [Configuration System](#5-configuration-system)
6. [Core Billing Engine Services](#6-core-billing-engine-services)
7. [Paystack Integration](#7-paystack-integration)
8. [Background Services](#8-background-services)
9. [Tenant Billing Portal](#9-tenant-billing-portal)
10. [Super Admin Billing Management](#10-super-admin-billing-management)
11. [Database Migration](#11-database-migration)
12. [Testing Strategy](#12-testing-strategy)
13. [Implementation Phases](#13-implementation-phases)
14. [Architecture Decisions](#14-architecture-decisions)

---

## 1. Overview

### Goal

Build a complete, flexible SaaS billing engine on top of Paystack that supports every common SaaS pricing model. The engine must be configurable per-app via `appsettings.json` so that each product built with this starter kit can define its own pricing model without code changes.

### Supported Billing Models

| Model | Description | Example |
|---|---|---|
| **Flat-rate subscription** | Fixed monthly or annual price per plan tier | R199/mo for Starter |
| **Per-seat / per-user** | Base price + per-seat charge above included seats | R99/mo + R29/seat above 5 |
| **Per-entity** | Charge per business object (projects, stores, etc.) | R10/project/month |
| **Usage-based / metered** | Charge based on consumption measured during billing period | R0.01 per API call above 10,000 |
| **Hybrid** | Flat base + usage/seat overages | R299/mo base + R0.05/API call over 50k |
| **One-off charges** | Setup fees, add-on purchases, manual charges | R500 setup fee |
| **Add-ons** | Optional extras purchasable independently of plan | R99/mo Priority Support |

### Paystack Strategy

- **Paystack Subscriptions** handle the fixed recurring amount (base plan charge).
- **Paystack `charge_authorization`** handles variable/overage charges (seats, usage, one-off) using saved card authorizations.
- **Two Paystack plans per local plan** — one for monthly, one for annual billing cycle.
- **Paystack Refund API** for actual refunds; internal credit ledger for proration/downgrades.

---

## 2. Current State Analysis

### Existing Entities

| Entity | File | Purpose |
|---|---|---|
| `Plan` | `src/Modules/Billing/Entities/Plan.cs` | Pricing tiers with `MonthlyPrice`, `AnnualPrice`, `MaxUsers`, `MaxRequestsPerMinute`, single `PaystackPlanCode` |
| `Subscription` | `src/Modules/Billing/Entities/Subscription.cs` | 1:1 with Tenant. Status enum, `BillingCycle`, `PaystackSubscriptionCode` (overloaded — stores tx reference initially, then SUB_ code), `PaystackCustomerCode` |
| `Invoice` | `src/Modules/Billing/Entities/Invoice.cs` | Single `Amount` field, no line items, no tax, no company details |
| `Payment` | `src/Modules/Billing/Entities/Payment.cs` | `PaystackReference`, `PaystackTransactionId`, `GatewayResponse` |
| `UsageRecord` | `src/Modules/Billing/Entities/UsageRecord.cs` | `Metric`, `Quantity`, `PeriodStart/End` — tracking only, not connected to billing |

### Existing Services

| Service | File | Lines | Purpose |
|---|---|---|---|
| `PaystackBillingService` | `src/Modules/Billing/Services/PaystackBillingService.cs` | ~1125 | Main billing implementation |
| `PaystackClient` | `src/Modules/Billing/Services/PaystackClient.cs` | — | Typed HttpClient wrapper for `https://api.paystack.co/` |
| `MockBillingService` | `src/Modules/Billing/Services/MockBillingService.cs` | ~278 | Dev mode — creates real DB records without Paystack calls |
| `InvoiceGenerator` | `src/Modules/Billing/Services/InvoiceGenerator.cs` | — | Sequential numbering: `INV-{YEAR}-{SEQ:D4}` |
| `UsageMeteringService` | `src/Modules/Billing/Services/UsageMeteringService.cs` | — | Records/queries usage per tenant/metric/month |
| `PlanSyncService` | `src/Infrastructure/Jobs/` | — | Background: syncs plans to Paystack every 60 min (monthly only) |
| `SubscriptionSyncService` | `src/Infrastructure/Jobs/` | — | Background: reconciles subscription statuses every 6 hr |

### Existing Webhook Handlers

The `PaystackWebhookController` at `POST /api/webhooks/paystack` handles 8 events:

1. `charge.success` — marks invoice paid, creates payment record
2. `subscription.create` — updates subscription with Paystack code
3. `subscription.not_renew` — sets status to NonRenewing
4. `subscription.disable` — sets status to Cancelled
5. `subscription.expiring_cards` — logs warning
6. `invoice.create` — creates local invoice
7. `invoice.update` — updates local invoice
8. `invoice.payment_failed` — marks invoice overdue

### Existing Feature Flags Integration

- `Feature` → `PlanFeature` (M:M with `ConfigJson`) → `TenantFeatureOverride`
- `TenantPlanFeatureFilter` evaluates access based on tenant's current plan
- Modules declare features with `MinPlanSlug` — features cascade to higher plans automatically

### Seeded Plans (CoreDataSeeder)

| Plan | Monthly | Annual | Max Users | Rate Limit |
|---|---|---|---|---|
| Free | R0 | — | 3 | 30/min |
| Starter | R199 | R1,990 | 10 | 60/min |
| Professional | R499 | R4,990 | 25 | 120/min |
| Enterprise | R999 | R9,990 | Unlimited | Unlimited |

### Known Limitations to Address

1. **Annual billing not implemented** — `AnnualPrice` column exists but `SyncPlansAsync` only creates monthly Paystack plans
2. **One subscription per tenant** — 1:1 relationship limits composability
3. **No usage-based billing** — `UsageMeteringService` tracks but never charges
4. **No coupon/discount system**
5. **Proration is approximate** — daily rate = `MonthlyPrice / 30`
6. **Plan change = cancel + re-subscribe** — no Paystack update, no credit system
7. **`PaystackSubscriptionCode` overloaded** — stores tx reference initially, later the real SUB_ code
8. **No webhook idempotency** — no deduplication, no dead-letter queue
9. **SuperAdmin plan change bypasses billing** — changes plan in DB without Paystack interaction
10. **Invoice numbering not concurrency-safe** — `SELECT MAX` without locking
11. **No tax/VAT calculation**
12. **No invoice line items** — single `Amount` field
13. **`ConfigJson` on `PlanFeature` unused**
14. **No grace period for failed payments** — immediate impact
15. **No saved authorization for recurring charges** — `authorization_code` not stored from successful transactions
16. **No refund support**
17. **No company/billing profile per tenant**

---

## 3. Paystack API Capabilities

### Subscriptions API

| Endpoint | Method | Purpose |
|---|---|---|
| `/plan` | POST | Create a plan (amount in kobo, interval: hourly/daily/weekly/monthly/quarterly/biannually/annually) |
| `/plan/{id_or_code}` | PUT | Update plan. `update_existing_subscriptions` flag available |
| `/plan` | GET | List all plans |
| `/subscription` | POST | Create subscription (customer, plan, authorization, start_date) |
| `/subscription/disable` | POST | Disable subscription (code + email_token) |
| `/subscription/{id_or_code}` | GET | Fetch subscription details |
| `/subscription/{id_or_code}/manage/link` | GET | Generate link for customer to update card |

**Key behaviors:**
- `invoice_limit` controls how many times a subscription charges (use for fixed-term)
- `start_date` enables delayed start / free trials
- Subscriptions are **NOT retried** on payment failure — we must own retry logic
- 5 statuses: `active`, `non-renewing`, `attention`, `completed`, `cancelled`

### Transactions API

| Endpoint | Method | Purpose |
|---|---|---|
| `/transaction/initialize` | POST | Initialize a payment (amount, email, callback_url, metadata, plan, channels) |
| `/transaction/verify/{reference}` | GET | Verify transaction status; returns authorization object |
| `/transaction/charge_authorization` | POST | Charge a saved card (authorization_code, email, amount). Supports `queue: true` for batch |
| `/transaction/{id}` | GET | Fetch transaction details |
| `/transaction/partial_debit` | POST | Charge what customer can afford (Mastercard/Verve only, by request) |

**Key behaviors:**
- `charge_authorization` is the workhorse for variable/overage billing
- Authorization object includes `authorization_code`, `reusable` (bool), `signature` (unique per card), `last4`, `bank`
- Some cards require 2FA challenge on recurring charge — response includes `paused: true` + `authorization_url`
- `metadata.custom_fields` array shows on Paystack dashboard
- `metadata.cancel_action` URL for cancelled checkouts
- `metadata.custom_filters.recurring: true` ensures card supports recurring

### Customers API

| Endpoint | Method | Purpose |
|---|---|---|
| `/customer` | POST | Create customer (email, first_name, last_name, phone, metadata) |
| `/customer/{email_or_code}` | GET | Fetch customer with authorizations, subscriptions, transactions |
| `/customer/{code}` | PUT | Update customer details |
| `/customer/set_risk_action` | POST | Whitelist/blacklist customer |
| `/customer/authorization/deactivate` | POST | Deactivate a saved authorization |

**Key behaviors:**
- Fetch customer returns all saved `authorizations` — use this to display payment methods
- Each authorization has a unique `signature` — use to deduplicate saved cards
- Only the email used to create an authorization can charge it

### Refunds API

| Endpoint | Method | Purpose |
|---|---|---|
| `/refund` | POST | Create refund (transaction reference, amount for partial) |
| `/refund` | GET | List all refunds |
| `/refund/{id}` | GET | Fetch single refund |

**Key behaviors:**
- Refund amount must not exceed original transaction amount
- Statuses: `pending` → `processing` → `processed` (or `failed`, `needs-attention`)
- Webhook events: `refund.pending`, `refund.processing`, `refund.processed`, `refund.failed`, `refund.needs-attention`
- Processed refunds may take up to 10 business days to reach customer

### Charges API

| Endpoint | Method | Purpose |
|---|---|---|
| `/charge` | POST | Initiate charge on specific channel (bank, card, ussd, qr, mobile_money) |
| `/charge/{reference}` | GET | Check pending charge status |
| `/charge/submit_pin` | POST | Submit PIN for charge |
| `/charge/submit_otp` | POST | Submit OTP for charge |

**Key behaviors:**
- QR code payments (`scan-to-pay`) supported in South Africa
- Multiple channels configurable: card, bank, ussd, qr, mobile_money, bank_transfer, eft

### Webhook Events

| Event | When |
|---|---|
| `charge.success` | Successful payment. **Must extract authorization object here.** |
| `subscription.create` | New subscription created |
| `subscription.not_renew` | Subscription marked as non-renewing |
| `subscription.disable` | Subscription disabled/cancelled |
| `subscription.expiring_cards` | Monthly: cards expiring this month |
| `invoice.create` | Invoice created for subscription (3 days before due) |
| `invoice.update` | Invoice updated (usually: payment succeeded) |
| `invoice.payment_failed` | Subscription payment failed |
| `refund.pending` | Refund initiated |
| `refund.processing` | Refund received by processor |
| `refund.processed` | Refund completed |
| `refund.failed` | Refund failed — account credited back |
| `refund.needs-attention` | Need customer bank details to process |
| `charge.dispute.create` | Dispute/chargeback logged |
| `charge.dispute.resolve` | Dispute resolved |

**Retry behavior:**
- Live mode: retried every 3 min for first 4 tries, then hourly for 72 hours
- Test mode: retried hourly for 10 hours, 30s timeout
- Must return `200 OK` quickly — offload long tasks to background queue

### Metadata

```json
{
  "metadata": {
    "tenant_id": "guid",
    "invoice_id": "guid",
    "charge_type": "subscription|seat_change|usage|one_off|setup_fee",
    "custom_fields": [
      {
        "display_name": "Tenant",
        "variable_name": "tenant_name",
        "value": "Acme Corp"
      },
      {
        "display_name": "Invoice",
        "variable_name": "invoice_number",
        "value": "INV-2026-0042"
      }
    ],
    "custom_filters": {
      "recurring": true
    }
  }
}
```

All transactions initialize with `custom_filters.recurring: true` to ensure saved cards support recurring billing.

---

## 4. Entity Model

### 4.1 Plan (evolved)

```csharp
public class Plan : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Pricing
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public string Currency { get; set; } = "ZAR";
    public BillingModel BillingModel { get; set; } = BillingModel.FlatRate;
    
    // Per-seat pricing (when BillingModel = PerSeat or Hybrid)
    public int? IncludedSeats { get; set; }               // seats included in base price
    public decimal? PerSeatMonthlyPrice { get; set; }      // price per additional seat/month
    public decimal? PerSeatAnnualPrice { get; set; }       // price per additional seat/year
    
    // One-time
    public decimal? SetupFee { get; set; }                 // charged once on first subscribe
    
    // Trial
    public int? TrialDays { get; set; }                    // overrides global Billing:TrialDays
    
    // Limits
    public int? MaxUsers { get; set; }                     // hard cap (null = unlimited)
    public int? MaxRequestsPerMinute { get; set; }
    
    // Display
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    
    // Paystack — one plan code per interval
    public string? PaystackMonthlyPlanCode { get; set; }
    public string? PaystackAnnualPlanCode { get; set; }
    
    // Navigation
    public ICollection<PlanFeature> PlanFeatures { get; set; } = [];
    public ICollection<PlanPricingTier> PricingTiers { get; set; } = [];
    public ICollection<Tenant> Tenants { get; set; } = [];
    
    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    
    // Computed
    public bool IsFreePlan => MonthlyPrice == 0;
}

public enum BillingModel
{
    FlatRate,       // Fixed price per interval
    PerSeat,        // Base price + per-seat charges
    UsageBased,     // Entirely usage-driven (base may be 0)
    Hybrid          // Flat base + usage/seat overages
}
```

### 4.2 PlanPricingTier (new)

Volume/tiered per-seat pricing. Optional — only used when a plan needs graduated pricing.

```csharp
public class PlanPricingTier
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    
    public int MinUnits { get; set; }       // e.g., 1
    public int? MaxUnits { get; set; }      // e.g., 10 (null = unlimited)
    public decimal PricePerUnit { get; set; } // e.g., R49
    
    // Tiers are evaluated in MinUnits order
    // Example: seats 1-10 = R49/seat, 11-25 = R39/seat, 26+ = R29/seat
}
```

### 4.3 AddOn (new)

Purchasable extras independent of plan.

```csharp
public class AddOn : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "ZAR";
    public AddOnInterval BillingInterval { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    
    // Paystack (for recurring add-ons)
    public string? PaystackPlanCode { get; set; }
    
    // Navigation
    public ICollection<TenantAddOn> TenantAddOns { get; set; } = [];
    
    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum AddOnInterval
{
    OneOff,
    Monthly,
    Annual
}
```

### 4.4 TenantAddOn (new)

Join entity between Tenant and AddOn.

```csharp
public class TenantAddOn
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid AddOnId { get; set; }
    public AddOn AddOn { get; set; } = null!;
    
    public int Quantity { get; set; } = 1;
    public DateTime ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    
    // Paystack (for recurring add-ons)
    public string? PaystackSubscriptionCode { get; set; }
}
```

### 4.5 Subscription (evolved)

```csharp
public class Subscription : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    
    // Status
    public SubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; }
    
    // Dates
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? GracePeriodEndsAt { get; set; }
    
    // Per-seat tracking
    public int Quantity { get; set; } = 1;  // seat count
    
    // Paystack identifiers
    public string? PaystackSubscriptionCode { get; set; }  // SUB_xxx only (no more overloading)
    public string? PaystackCustomerCode { get; set; }       // CUS_xxx
    public string? PaystackAuthorizationCode { get; set; }  // AUTH_xxx — for charge_authorization
    public string? PaystackEmailToken { get; set; }         // email token for subscription management
    public string? PaystackAuthorizationEmail { get; set; } // email used when auth was created
    
    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum SubscriptionStatus
{
    Active,
    PastDue,      // payment failed, in grace period
    Cancelled,
    Expired,
    Trialing,
    NonRenewing   // will cancel at end of current period
}

public enum BillingCycle
{
    Monthly,
    Annual
}
```

### 4.6 Invoice (evolved)

```csharp
public class Invoice : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }
    
    // Invoice identity
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; }
    
    // Amounts (all in Currency)
    public decimal Subtotal { get; set; }        // sum of line items before tax/discounts
    public decimal DiscountAmount { get; set; }   // total discounts applied
    public decimal TaxAmount { get; set; }        // VAT/tax amount
    public decimal TaxRate { get; set; }          // rate applied (e.g., 0.15)
    public decimal CreditApplied { get; set; }    // credits consumed
    public decimal Total { get; set; }            // final amount due
    public string Currency { get; set; } = "ZAR";
    
    // Dates
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidDate { get; set; }
    public DateTime? BillingPeriodStart { get; set; }
    public DateTime? BillingPeriodEnd { get; set; }
    
    // Company details (snapshot at invoice time)
    public string? CompanyName { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyVatNumber { get; set; }
    
    // Customer details (snapshot at invoice time)
    public string? TenantCompanyName { get; set; }
    public string? TenantBillingAddress { get; set; }
    public string? TenantVatNumber { get; set; }
    
    // Description (legacy / simple summary)
    public string? Description { get; set; }
    
    // Paystack
    public string? PaystackReference { get; set; }
    
    // Navigation
    public ICollection<InvoiceLineItem> LineItems { get; set; } = [];
    public Payment? Payment { get; set; }
    
    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum InvoiceStatus
{
    Draft,
    Issued,
    Paid,
    Overdue,
    Cancelled,
    Refunded,
    PartiallyRefunded
}
```

### 4.7 InvoiceLineItem (new)

Every charge is broken into transparent line items.

```csharp
public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    
    public LineItemType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }  // Quantity × UnitPrice (or negative for credits/discounts)
    
    // Optional references
    public Guid? AddOnId { get; set; }
    public string? UsageMetric { get; set; }
    
    public int SortOrder { get; set; }
}

public enum LineItemType
{
    Subscription,    // base plan charge
    Seat,            // per-seat charge
    UsageCharge,     // metered usage overage
    AddOn,           // add-on subscription or purchase
    SetupFee,        // one-time setup fee
    OneOff,          // ad-hoc manual charge
    Discount,        // coupon/promo (negative amount)
    Credit,          // credit applied (negative amount)
    Tax,             // VAT/tax line
    Proration        // prorated charge or credit for mid-cycle changes
}
```

### 4.8 Payment (unchanged)

```csharp
public class Payment : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public PaymentStatus Status { get; set; }
    public string? PaystackReference { get; set; }
    public string? PaystackTransactionId { get; set; }
    public string? GatewayResponse { get; set; }
    public DateTime TransactionDate { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum PaymentStatus
{
    Pending,
    Success,
    Failed,
    Refunded,
    PartiallyRefunded
}
```

### 4.9 UsageRecord (unchanged)

```csharp
public class UsageRecord : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public long Quantity { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

### 4.10 Discount (new)

Coupon/promotional code system.

```csharp
public class Discount : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;       // e.g., "LAUNCH50"
    public string Name { get; set; } = string.Empty;       // e.g., "Launch 50% Off"
    public string? Description { get; set; }
    
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }                      // percentage (0.50 = 50%) or fixed amount in cents
    public string Currency { get; set; } = "ZAR";          // relevant for FixedAmount type
    
    // Limits
    public int? MaxRedemptions { get; set; }                // null = unlimited
    public int CurrentRedemptions { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    
    // Scope
    public string? ApplicablePlanSlugs { get; set; }        // JSON array: ["starter", "professional"] or null = all
    public int? DurationInCycles { get; set; }              // null = forever, 3 = applies for 3 billing cycles
    
    public bool IsActive { get; set; } = true;
    
    // Navigation
    public ICollection<TenantDiscount> TenantDiscounts { get; set; } = [];
    
    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum DiscountType
{
    Percentage,   // Value = 0.50 means 50% off
    FixedAmount   // Value = 5000 means R50.00 off (stored in cents)
}
```

### 4.11 TenantDiscount (new)

Tracks which discounts are applied to which tenants.

```csharp
public class TenantDiscount
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid DiscountId { get; set; }
    public Discount Discount { get; set; } = null!;
    
    public DateTime AppliedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? RemainingCycles { get; set; }  // decremented each billing cycle; null = forever
    public bool IsActive { get; set; } = true;
}
```

### 4.12 TenantCredit (new)

Credit ledger for proration refunds, manual credits, and promotional credits.

```csharp
public class TenantCredit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    
    public decimal Amount { get; set; }         // always positive
    public string Currency { get; set; } = "ZAR";
    public CreditReason Reason { get; set; }
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid? ConsumedByInvoiceId { get; set; }
    public Invoice? ConsumedByInvoice { get; set; }
    
    // Partial consumption tracking
    public decimal RemainingAmount { get; set; }  // starts equal to Amount, decreases as consumed
}

public enum CreditReason
{
    PlanChangeCredit,  // unused days when downgrading/changing plans
    Refund,            // credit issued instead of monetary refund
    Manual,            // SuperAdmin manual credit
    Promotional        // promo/goodwill credit
}
```

### 4.13 BillingProfile (new)

Company/billing details per tenant for invoicing.

```csharp
public class BillingProfile
{
    public Guid TenantId { get; set; }  // PK, 1:1 with Tenant
    public Tenant Tenant { get; set; } = null!;
    
    public string? CompanyName { get; set; }
    public string? BillingAddress { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "ZA";
    public string? VatNumber { get; set; }
    public string? BillingEmail { get; set; }  // separate from contact email
}
```

### 4.14 WebhookEvent (new)

Idempotent webhook processing and audit trail.

```csharp
public class WebhookEvent
{
    public Guid Id { get; set; }
    public string PaystackEventType { get; set; } = string.Empty;  // e.g., "charge.success"
    public string PaystackReference { get; set; } = string.Empty;  // unique identifier from payload
    public string Payload { get; set; } = string.Empty;            // raw JSON
    
    public WebhookEventStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
    
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public enum WebhookEventStatus
{
    Received,
    Processing,
    Processed,
    Failed
}
```

### 4.15 Tenant (evolved)

```csharp
public class Tenant : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string? DatabaseName { get; set; }
    
    // Billing
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public Subscription? ActiveSubscription { get; set; }
    public BillingProfile? BillingProfile { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<TenantAddOn> AddOns { get; set; } = [];
    public ICollection<TenantDiscount> Discounts { get; set; } = [];
    public ICollection<TenantCredit> Credits { get; set; } = [];
    
    // Soft delete support
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? ScheduledDeletionAt { get; set; }
    
    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

> **Note**: `TrialEndsAt` moves from Tenant to `Subscription.TrialEndsAt` — trials are a property of the subscription, not the tenant.

### 4.16 PlanFeature (unchanged)

```csharp
public class PlanFeature
{
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public string? ConfigJson { get; set; }  // now used for feature limits: {"max_projects": 10}
}
```

> `ConfigJson` will now be utilized for feature-specific limits (e.g., max projects per plan, max storage GB). The `TenantPlanFeatureFilter` should be updated to read and enforce these limits.

### Entity Relationship Diagram

```
Plan ──1:M──> PlanFeature ──M:1──> Feature
 │                                    │
 │                                    └──> TenantFeatureOverride
 ├──1:M──> PlanPricingTier
 ├──1:M──> Tenant
 │           │
 │           ├──1:1──> Subscription
 │           ├──1:1──> BillingProfile
 │           ├──1:M──> Invoice ──1:M──> InvoiceLineItem
 │           │           └──1:1──> Payment
 │           ├──1:M──> TenantAddOn ──M:1──> AddOn
 │           ├──1:M──> TenantDiscount ──M:1──> Discount
 │           ├──1:M──> TenantCredit
 │           └──1:M──> UsageRecord
 │
 └──(via slug)──> Discount.ApplicablePlanSlugs

WebhookEvent (standalone — for idempotency)
```

---

## 5. Configuration System

### 5.1 appsettings.json Structure

```json
{
  "Billing": {
    "Provider": "Paystack",
    "Currency": "ZAR",
    
    "Tax": {
      "Rate": 0.15,
      "Label": "VAT",
      "Included": true
    },
    
    "Company": {
      "Name": "Your Company (Pty) Ltd",
      "Address": "123 Main Street, Cape Town, 8001, South Africa",
      "VatNumber": "4123456789"
    },
    
    "Invoice": {
      "Prefix": "INV",
      "PaymentTermDays": 0
    },
    
    "Trial": {
      "DefaultDays": 14
    },
    
    "GracePeriod": {
      "Days": 3,
      "DunningAttempts": 3,
      "DunningIntervalHours": 72
    },
    
    "Features": {
      "AnnualBilling": true,
      "PerSeatBilling": false,
      "UsageBilling": false,
      "AddOns": false,
      "Discounts": true,
      "SetupFees": false
    },
    
    "UsageMetrics": {
      "api_calls": {
        "DisplayName": "API Calls",
        "IncludedByPlan": {
          "free": 1000,
          "starter": 10000,
          "professional": 50000,
          "enterprise": null
        },
        "OveragePrice": 0.01
      },
      "storage_gb": {
        "DisplayName": "Storage (GB)",
        "IncludedByPlan": {
          "free": 1,
          "starter": 10,
          "professional": 50,
          "enterprise": null
        },
        "OveragePrice": 5.00
      }
    },
    
    "Paystack": {
      "SecretKey": "",
      "PublicKey": "",
      "WebhookSecret": "",
      "BaseUrl": "https://api.paystack.co"
    }
  }
}
```

### 5.2 BillingOptions Class

```csharp
public class BillingOptions
{
    public const string SectionName = "Billing";
    
    public string Provider { get; set; } = "Paystack";
    public string Currency { get; set; } = "ZAR";
    
    public TaxOptions Tax { get; set; } = new();
    public CompanyOptions Company { get; set; } = new();
    public InvoiceOptions Invoice { get; set; } = new();
    public TrialOptions Trial { get; set; } = new();
    public GracePeriodOptions GracePeriod { get; set; } = new();
    public BillingFeatureToggles Features { get; set; } = new();
    public Dictionary<string, UsageMetricConfig> UsageMetrics { get; set; } = new();
    public PaystackOptions Paystack { get; set; } = new();
}

public class TaxOptions
{
    public decimal Rate { get; set; } = 0.15m;
    public string Label { get; set; } = "VAT";
    public bool Included { get; set; } = true;  // true = prices are VAT-inclusive
}

public class CompanyOptions
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
}

public class InvoiceOptions
{
    public string Prefix { get; set; } = "INV";
    public int PaymentTermDays { get; set; } = 0;
}

public class TrialOptions
{
    public int? DefaultDays { get; set; } = 14;
}

public class GracePeriodOptions
{
    public int Days { get; set; } = 3;
    public int DunningAttempts { get; set; } = 3;
    public int DunningIntervalHours { get; set; } = 72;
}

public class BillingFeatureToggles
{
    public bool AnnualBilling { get; set; } = true;
    public bool PerSeatBilling { get; set; } = false;
    public bool UsageBilling { get; set; } = false;
    public bool AddOns { get; set; } = false;
    public bool Discounts { get; set; } = true;
    public bool SetupFees { get; set; } = false;
}

public class UsageMetricConfig
{
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, long?> IncludedByPlan { get; set; } = new();  // null = unlimited
    public decimal OveragePrice { get; set; }
}

public class PaystackOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? WebhookSecret { get; set; }
    public string BaseUrl { get; set; } = "https://api.paystack.co";
}
```

### 5.3 Registration

```csharp
// In BillingModule.cs or ServiceCollectionExtensions
services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));
```

---

## 6. Core Billing Engine Services

### 6.1 IBillingService (redesigned)

```csharp
public interface IBillingService
{
    // Subscriptions
    Task<SubscriptionInitResult> InitializeSubscriptionAsync(SubscriptionInitRequest request);
    Task<SubscriptionStatus?> GetSubscriptionStatusAsync(Guid tenantId);
    Task<bool> CancelSubscriptionAsync(Guid tenantId);
    Task<PlanChangeResult> ChangePlanAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null);
    Task<PlanChangePreview> PreviewPlanChangeAsync(Guid tenantId, Guid newPlanId, BillingCycle? newCycle = null);
    
    // Seat management
    Task<SeatChangeResult> UpdateSeatCountAsync(Guid tenantId, int newSeatCount);
    Task<SeatChangePreview> PreviewSeatChangeAsync(Guid tenantId, int newSeatCount);
    
    // One-off charges
    Task<ChargeResult> ChargeOneOffAsync(Guid tenantId, decimal amount, string description);
    
    // Refunds
    Task<RefundResult> IssueRefundAsync(Guid paymentId, decimal? amount = null);
    
    // Discounts
    Task<DiscountResult> ApplyDiscountAsync(Guid tenantId, string discountCode);
    
    // Usage billing
    Task<UsageBillingResult> ProcessUsageBillingAsync(Guid tenantId);
    
    // Paystack sync
    Task SyncPlansAsync();
    Task<bool> UpdatePlanInGatewayAsync(Guid planId);
    Task ReconcileSubscriptionsAsync();
    
    // Webhooks
    Task<WebhookResult> ProcessWebhookAsync(string payload, string signature);
    Task VerifyAndLinkSubscriptionAsync(string reference);
    
    // Customer portal
    Task<string?> GetManageLinkAsync(Guid tenantId);
    Task<BillingDashboard> GetBillingDashboardAsync(Guid tenantId);
}
```

#### Supporting Records

```csharp
public record SubscriptionInitRequest(
    Guid TenantId,
    string Email,
    Guid PlanId,
    BillingCycle BillingCycle,
    int SeatCount = 1,
    string? DiscountCode = null,
    string? CallbackUrl = null
);

public record SubscriptionInitResult(
    bool Success,
    string? PaymentUrl = null,
    string? Error = null,
    bool RequiresRedirect = true
);

public record PlanChangeResult(
    bool Success,
    string? PaymentUrl = null,
    decimal CreditApplied = 0,
    decimal AmountCharged = 0,
    string? Error = null
);

public record PlanChangePreview(
    bool IsValid,
    string? Error = null,
    string CurrentPlanName = "",
    string NewPlanName = "",
    decimal CurrentPlanPrice = 0,
    decimal NewPlanPrice = 0,
    BillingCycle CurrentCycle = BillingCycle.Monthly,
    BillingCycle NewCycle = BillingCycle.Monthly,
    int RemainingDays = 0,
    int TotalCycleDays = 30,
    decimal UnusedCredit = 0,
    decimal ProratedNewCost = 0,
    decimal AmountDue = 0,
    bool IsUpgrade = false,
    decimal CreditForNextCycle = 0
);

public record SeatChangeResult(
    bool Success,
    int PreviousSeats = 0,
    int NewSeats = 0,
    decimal AmountCharged = 0,
    decimal CreditIssued = 0,
    string? Error = null
);

public record SeatChangePreview(
    bool IsValid,
    int CurrentSeats = 0,
    int NewSeats = 0,
    int SeatDifference = 0,
    decimal PricePerSeat = 0,
    int RemainingDays = 0,
    int TotalCycleDays = 30,
    decimal ProratedAmount = 0,
    bool IsIncrease = true,
    string? Error = null
);

public record ChargeResult(
    bool Success,
    Guid? InvoiceId = null,
    Guid? PaymentId = null,
    string? PaymentUrl = null,
    string? Error = null
);

public record RefundResult(
    bool Success,
    decimal AmountRefunded = 0,
    string? PaystackRefundReference = null,
    string? Error = null
);

public record DiscountResult(
    bool Success,
    string? DiscountName = null,
    decimal? DiscountValue = null,
    DiscountType? Type = null,
    string? Error = null
);

public record UsageBillingResult(
    bool Success,
    Guid? InvoiceId = null,
    decimal TotalUsageCharge = 0,
    Dictionary<string, UsageChargeLine> UsageBreakdown = null!,
    string? Error = null
);

public record UsageChargeLine(
    string MetricDisplayName,
    long IncludedQuantity,
    long ActualQuantity,
    long OverageQuantity,
    decimal PricePerUnit,
    decimal TotalCharge
);

public record WebhookResult(bool Success, string? Error = null);

public record BillingDashboard(
    // Current plan
    string PlanName,
    string PlanSlug,
    BillingCycle BillingCycle,
    decimal CurrentPrice,
    
    // Subscription
    SubscriptionStatus Status,
    DateTime? NextBillingDate,
    DateTime? TrialEndsAt,
    bool IsTrialing,
    
    // Seats
    int CurrentSeats,
    int? IncludedSeats,
    int? MaxSeats,
    decimal? PerSeatPrice,
    
    // Financials
    decimal CreditBalance,
    decimal EstimatedNextInvoice,
    
    // Usage (if enabled)
    List<UsageSummaryLine>? UsageSummary,
    
    // Active add-ons
    List<ActiveAddOnLine>? ActiveAddOns,
    
    // Active discounts
    List<ActiveDiscountLine>? ActiveDiscounts,
    
    // Recent invoices
    List<InvoiceSummaryLine> RecentInvoices,
    
    // Payment methods
    List<PaymentMethodLine> PaymentMethods
);

public record UsageSummaryLine(string Metric, string DisplayName, long Used, long? Included, decimal OverageCharge);
public record ActiveAddOnLine(string Name, decimal Price, AddOnInterval Interval, DateTime ActivatedAt);
public record ActiveDiscountLine(string Name, string Code, DiscountType Type, decimal Value, int? RemainingCycles);
public record InvoiceSummaryLine(string InvoiceNumber, decimal Total, InvoiceStatus Status, DateTime IssuedDate);
public record PaymentMethodLine(string Last4, string CardType, string Bank, string ExpiryMonth, string ExpiryYear, bool IsDefault);
```

### 6.2 InvoiceEngine

Replaces the simple `InvoiceGenerator`. Responsible for all invoice creation, line item assembly, tax calculation, and numbering.

```
InvoiceEngine
├── GenerateSubscriptionInvoiceAsync(tenantId, billingPeriod)
│   ├── Add line: base plan charge (LineItemType.Subscription)
│   ├── Add line: per-seat charges if applicable (LineItemType.Seat)
│   ├── Add lines: active add-on charges (LineItemType.AddOn)
│   ├── Add lines: usage overage charges if applicable (LineItemType.UsageCharge)
│   ├── Add line: setup fee if first invoice (LineItemType.SetupFee)
│   ├── Calculate subtotal
│   ├── Apply discounts → add line (LineItemType.Discount, negative)
│   ├── Calculate tax on (subtotal - discounts)
│   │   ├── If Tax.Included = true: tax = total × rate / (1 + rate)
│   │   └── If Tax.Included = false: tax = (subtotal - discounts) × rate
│   ├── Add line: tax (LineItemType.Tax)
│   ├── Apply credits → add line (LineItemType.Credit, negative)
│   ├── Calculate total
│   ├── Snapshot company + tenant billing details
│   └── Generate invoice number (concurrency-safe)
│
├── GenerateOneOffInvoiceAsync(tenantId, description, amount)
│   └── Single line item (LineItemType.OneOff) + tax
│
├── GenerateProrationInvoiceAsync(tenantId, description, lineItems)
│   └── For mid-cycle seat/plan changes
│
├── FinalizeInvoiceAsync(invoiceId)
│   ├── Set status = Issued
│   └── Queue email notification
│
└── VoidInvoiceAsync(invoiceId)
    └── Set status = Cancelled
```

**Invoice Number Generation** (concurrency-safe for SQLite):

```csharp
// Use a dedicated sequence table or optimistic retry
// Format: {Prefix}-{YEAR}-{SEQ:D5}  e.g., INV-2026-00042
public async Task<string> GenerateInvoiceNumberAsync()
{
    var year = DateTime.UtcNow.Year;
    var prefix = _options.Invoice.Prefix;
    
    // Retry loop for SQLite concurrency
    for (int attempt = 0; attempt < 3; attempt++)
    {
        var maxSeq = await _db.Invoices
            .Where(i => i.InvoiceNumber.StartsWith($"{prefix}-{year}-"))
            .MaxAsync(i => (int?)Convert.ToInt32(i.InvoiceNumber.Substring(prefix.Length + 6))) ?? 0;
        
        var newNumber = $"{prefix}-{year}-{(maxSeq + 1):D5}";
        
        // Attempt to save — if duplicate, retry
        try { /* save */ return newNumber; }
        catch (DbUpdateException) { /* retry */ }
    }
}
```

### 6.3 CreditService

```
CreditService
├── AddCreditAsync(tenantId, amount, reason, description?)
│   └── Creates TenantCredit with RemainingAmount = Amount
│
├── ApplyCreditsToInvoiceAsync(tenantId, invoiceId)
│   ├── Get all unconsumed credits (RemainingAmount > 0) ordered by CreatedAt
│   ├── Apply credits up to invoice total
│   ├── Update RemainingAmount on each credit
│   ├── Set ConsumedAt and ConsumedByInvoiceId when fully consumed
│   ├── Add InvoiceLineItem(Type = Credit, negative amount)
│   └── Update invoice.CreditApplied and invoice.Total
│
├── GetBalanceAsync(tenantId)
│   └── SUM(RemainingAmount) where RemainingAmount > 0
│
└── GetLedgerAsync(tenantId)
    └── Returns all credits with consumption history
```

### 6.4 DiscountService

```
DiscountService
├── ValidateCodeAsync(code, tenantId, planId)
│   ├── Check: discount exists and IsActive
│   ├── Check: not expired (ValidFrom/ValidUntil)
│   ├── Check: MaxRedemptions not reached
│   ├── Check: plan is in ApplicablePlanSlugs (or null = all)
│   ├── Check: tenant doesn't already have this discount active
│   └── Returns: validation result with discount details
│
├── ApplyAsync(tenantId, code)
│   ├── Validate
│   ├── Create TenantDiscount (RemainingCycles = DurationInCycles)
│   └── Increment Discount.CurrentRedemptions
│
├── CalculateDiscountAsync(tenantId, subtotal)
│   ├── Get all active TenantDiscounts for tenant
│   ├── For each: calculate discount amount
│   │   ├── Percentage: subtotal × Value
│   │   └── FixedAmount: Value (capped at subtotal)
│   ├── Stack discounts (sum all, cap at subtotal)
│   └── Returns: total discount amount
│
├── DecrementCyclesAsync(tenantId)
│   ├── Called after each billing cycle
│   ├── Decrement RemainingCycles for active discounts
│   └── Deactivate discounts where RemainingCycles = 0
│
└── RemoveAsync(tenantId, discountId)
    └── Set TenantDiscount.IsActive = false
```

### 6.5 SeatBillingService

```
SeatBillingService
├── UpdateSeatsAsync(tenantId, newCount)
│   ├── Validate: newCount >= 1
│   ├── Validate: newCount <= Plan.MaxUsers (if set)
│   ├── Calculate prorated amount for remaining days in cycle
│   │   ├── Get days remaining until NextBillingDate
│   │   ├── Get total days in cycle (30 for monthly, 365 for annual)
│   │   ├── seatDiff = newCount - currentCount
│   │   ├── pricePerSeat = Plan.PerSeatMonthlyPrice or PerSeatAnnualPrice
│   │   │   └── If PricingTiers exist, use tiered calculation
│   │   └── proratedAmount = seatDiff × pricePerSeat × (remainingDays / totalDays)
│   ├── If increasing seats (proratedAmount > 0):
│   │   ├── Create prorated invoice with line items
│   │   ├── Charge via charge_authorization
│   │   └── Update Subscription.Quantity
│   ├── If decreasing seats (proratedAmount < 0):
│   │   ├── Issue credit for |proratedAmount|
│   │   └── Update Subscription.Quantity (effective immediately)
│   └── Return SeatChangeResult
│
├── PreviewSeatChangeAsync(tenantId, newCount)
│   └── Same calculation without executing
│
└── CalculateTieredSeatPrice(plan, seatCount)
    ├── If no PricingTiers: return seatCount × PerSeatPrice
    └── If PricingTiers:
        ├── Order tiers by MinUnits
        ├── Apply each tier's rate to the units in that bracket
        └── Return total (graduated pricing)
```

**Tiered pricing example:**

| Tier | Min | Max | Price/seat |
|---|---|---|---|
| 1 | 1 | 10 | R49 |
| 2 | 11 | 25 | R39 |
| 3 | 26 | null | R29 |

For 15 seats: (10 × R49) + (5 × R39) = R490 + R195 = R685

### 6.6 UsageBillingService

Extends the existing `UsageMeteringService` to actually charge for overages.

```
UsageBillingService
├── RecordUsageAsync(tenantId, metric, quantity)
│   └── Existing functionality — writes UsageRecord
│
├── GetCurrentPeriodUsageAsync(tenantId)
│   └── Returns: Dictionary<metric, totalQuantity> for current billing period
│
├── CalculateUsageChargesAsync(tenantId, periodStart, periodEnd)
│   ├── For each configured metric in BillingOptions.UsageMetrics:
│   │   ├── Get total usage for period from UsageRecords
│   │   ├── Get included quantity for tenant's plan
│   │   ├── If included = null (unlimited): overage = 0
│   │   ├── Otherwise: overage = MAX(0, actual - included)
│   │   └── charge = overage × OveragePrice
│   └── Returns: list of UsageChargeLine
│
├── ProcessEndOfPeriodAsync(tenantId)
│   ├── Calculate usage charges
│   ├── If total > 0:
│   │   ├── Add usage line items to subscription invoice
│   │   └── (Charge happens when subscription invoice is processed)
│   └── If standalone usage billing:
│       ├── Create separate usage invoice
│       └── Charge via charge_authorization
│
└── GetUsageSummaryAsync(tenantId)
    └── Returns: current period usage vs. quotas with visual data
```

**Configuration-driven metrics:**

```json
"UsageMetrics": {
  "api_calls": {
    "DisplayName": "API Calls",
    "IncludedByPlan": { "free": 1000, "starter": 10000, "professional": 50000, "enterprise": null },
    "OveragePrice": 0.01
  }
}
```

### 6.7 DunningService

Handles payment failure recovery.

```
DunningService
├── OnPaymentFailedAsync(tenantId, invoiceId)
│   ├── Set Subscription.Status = PastDue
│   ├── Set Subscription.GracePeriodEndsAt = now + GracePeriod.Days
│   ├── Send "payment failed" email to tenant
│   └── Log event
│
├── RetryChargeAsync(tenantId)
│   ├── Get latest failed invoice
│   ├── Attempt charge via charge_authorization
│   ├── If success:
│   │   ├── Mark invoice Paid
│   │   ├── Set Subscription.Status = Active
│   │   ├── Clear GracePeriodEndsAt
│   │   └── Send "payment recovered" email
│   └── If failure:
│       ├── Increment failure count
│       └── Send escalation email
│
├── ProcessGracePeriodsAsync()  // called by background job
│   ├── Find subscriptions where GracePeriodEndsAt <= now
│   ├── For each:
│   │   ├── Set Subscription.Status = Cancelled
│   │   ├── Set Tenant.Status = Suspended
│   │   ├── Cancel Paystack subscription
│   │   └── Send "account suspended" email
│   └── Find subscriptions in PastDue needing retry:
│       └── Attempt RetryChargeAsync based on DunningIntervalHours
│
└── ReactivateAsync(tenantId)
    ├── Called when suspended tenant pays manually
    ├── Restore Subscription.Status = Active
    └── Restore Tenant.Status = Active
```

### 6.8 AddOnService

```
AddOnService
├── SubscribeAsync(tenantId, addOnId, quantity = 1)
│   ├── For OneOff: charge immediately via charge_authorization → create invoice
│   ├── For Monthly/Annual: create Paystack subscription for add-on plan
│   └── Create TenantAddOn record
│
├── UnsubscribeAsync(tenantId, addOnId)
│   ├── Disable Paystack subscription if recurring
│   ├── Set TenantAddOn.DeactivatedAt = now
│   └── Calculate and issue credit for remaining period
│
├── ListAvailableAsync(tenantId)
│   └── Returns active add-ons not already subscribed
│
└── ListActiveAsync(tenantId)
    └── Returns tenant's active add-ons with billing details
```

---

## 7. Paystack Integration

### 7.1 PaystackClient (evolved)

Add these methods to the existing typed HttpClient:

```csharp
public class PaystackClient
{
    // Existing
    Task<PaystackPlanResponse> CreatePlanAsync(string name, int amountInKobo, string interval, ...);
    Task<List<PaystackPlanResponse>> ListPlansAsync();
    Task<bool> UpdatePlanAsync(string planCode, string? name, int? amountInKobo, ...);
    Task<PaystackTransactionResponse> InitializeTransactionAsync(string email, int amountInKobo, ...);
    Task<PaystackTransactionResponse> VerifyTransactionAsync(string reference);
    Task<PaystackSubscriptionResponse> CreateSubscriptionAsync(string customer, string plan, string? authorization, DateTime? startDate);
    Task<bool> DisableSubscriptionAsync(string code, string emailToken);
    Task<List<PaystackSubscriptionResponse>> ListSubscriptionsAsync(int page, int perPage);
    Task<PaystackSubscriptionResponse> FetchSubscriptionAsync(string idOrCode);
    Task<PaystackCustomerResponse> CreateCustomerAsync(string email, string? firstName, string? lastName);
    
    // New — Recurring charges
    Task<PaystackChargeResponse> ChargeAuthorizationAsync(
        string authorizationCode, 
        string email, 
        int amountInKobo,
        string? reference = null,
        object? metadata = null,
        bool queue = false);
    
    // New — Refunds
    Task<PaystackRefundResponse> CreateRefundAsync(string transactionReference, int? amountInKobo = null);
    Task<PaystackRefundResponse> FetchRefundAsync(string refundId);
    Task<List<PaystackRefundResponse>> ListRefundsAsync(int page = 1, int perPage = 50);
    
    // New — Customer details
    Task<PaystackCustomerDetailResponse> FetchCustomerAsync(string emailOrCode);
    Task<bool> DeactivateAuthorizationAsync(string authorizationCode);
    
    // New — Subscription management
    Task<PaystackManageLinkResponse> GenerateManageLinkAsync(string subscriptionCode);
}
```

### 7.2 PaystackBillingService Changes

#### Plan Sync — Create Two Plans Per Local Plan

```
SyncPlansAsync()
├── For each active local plan where MonthlyPrice > 0:
│   ├── If PaystackMonthlyPlanCode is null:
│   │   ├── Create Paystack plan: name="{PlanName} (Monthly)", interval="monthly", amount=MonthlyPrice×100
│   │   └── Store PaystackMonthlyPlanCode
│   ├── Else:
│   │   └── Update Paystack plan if name/amount changed
│   │
│   ├── If AnnualPrice > 0 AND Features.AnnualBilling:
│   │   ├── If PaystackAnnualPlanCode is null:
│   │   │   ├── Create Paystack plan: name="{PlanName} (Annual)", interval="annually", amount=AnnualPrice×100
│   │   │   └── Store PaystackAnnualPlanCode
│   │   └── Else:
│   │       └── Update Paystack plan if name/amount changed
│   └── Done
└── Log summary
```

#### Initialize Subscription — With Trials and Setup Fees

```
InitializeSubscriptionAsync(request)
├── Load plan
├── If plan.IsFreePlan:
│   ├── Create local subscription (Status = Active, no Paystack)
│   └── Return success (RequiresRedirect = false)
│
├── Determine Paystack plan code:
│   ├── Monthly → plan.PaystackMonthlyPlanCode
│   └── Annual → plan.PaystackAnnualPlanCode
│
├── Calculate amount:
│   ├── Base: plan price for selected cycle
│   ├── + Setup fee (if plan.SetupFee and first-time)
│   ├── + Per-seat (if seats > IncludedSeats)
│   └── Note: Paystack subscription handles base; extras via charge_authorization after
│
├── Determine start_date:
│   ├── If trial: now + (plan.TrialDays ?? globalTrialDays)
│   └── Else: null (immediate)
│
├── Build metadata:
│   ├── tenant_id, charge_type="subscription"
│   ├── custom_fields for dashboard visibility
│   └── custom_filters.recurring = true
│
├── Initialize transaction:
│   ├── POST /transaction/initialize
│   ├── email, amount, plan=planCode, callback_url
│   ├── start_date (for trial)
│   ├── metadata
│   └── Get authorization_url
│
├── Create local subscription (Status = Trialing or Active, pending Paystack confirmation)
├── Create local invoice (Status = Draft)
└── Return { Success, PaymentUrl = authorization_url }
```

#### Store Authorization on charge.success

```
HandleChargeSuccess(payload)
├── Extract authorization object
├── If authorization.reusable == true:
│   ├── Find subscription for this tenant
│   ├── Store authorization_code
│   ├── Store authorization email
│   └── Log: "Saved reusable authorization for tenant {id}"
├── Find/create invoice, mark as Paid
├── Create Payment record
└── Update subscription status
```

#### Plan Change — With Credits and Proration

```
ChangePlanAsync(tenantId, newPlanId, newCycle?)
├── Load current subscription + plan, new plan
├── Cancel current Paystack subscription
├── Calculate credit for unused days:
│   ├── remainingDays = (nextBillingDate - now).Days
│   ├── dailyRate = currentPrice / cycleDays
│   ├── credit = remainingDays × dailyRate
│   └── AddCredit(tenantId, credit, PlanChangeCredit)
│
├── If new plan is free:
│   ├── Create local subscription (Active, no Paystack)
│   └── Return success
│
├── Calculate amount due for new plan:
│   ├── Full new plan price for first cycle
│   ├── Minus available credits
│   └── amountDue = MAX(0, newPrice - creditBalance)
│
├── If amountDue > 0 AND saved authorization exists:
│   ├── Try charge_authorization
│   ├── If success: create subscription + invoice + payment
│   ├── If paused (2FA required): return PaymentUrl for authorization
│   └── If failed: initialize new transaction with redirect
│
├── If no saved authorization:
│   └── Initialize new transaction (redirect to Paystack checkout)
│
├── Create new local subscription
├── Update tenant.PlanId
└── Return result
```

#### charge_authorization for Variable Charges

```
ChargeViaSavedAuth(tenantId, amountInCents, description, chargeType, metadata)
├── Load subscription → get PaystackAuthorizationCode + PaystackAuthorizationEmail
├── If no authorization saved:
│   └── Return error: "No saved payment method"
│
├── Generate unique reference
├── Build metadata: tenant_id, charge_type, invoice_id, custom_fields
│
├── POST /transaction/charge_authorization:
│   ├── authorization_code
│   ├── email (must match authorization email)
│   ├── amount (in kobo/cents)
│   ├── reference
│   ├── metadata
│   └── queue: true (if batch)
│
├── If response.status == "success":
│   ├── Create Payment record
│   └── Return success
├── If response.data.paused == true:
│   └── Return { PaymentUrl = response.data.authorization_url } (2FA required)
└── If failed:
    └── Return error with gateway_response
```

### 7.3 Webhook Controller Overhaul

```
ProcessWebhookAsync(payload, signature)
├── 1. Verify HMAC-SHA512 signature
│
├── 2. Parse event type + reference from payload
│
├── 3. Idempotency check:
│   ├── Look up WebhookEvent by reference + eventType
│   ├── If already Processed: return success (skip)
│   └── If not found: create WebhookEvent(Status = Received)
│
├── 4. Process by event type:
│   ├── charge.success → HandleChargeSuccessAsync
│   │   ├── Extract authorization → store if reusable
│   │   ├── Match to invoice via reference/metadata
│   │   ├── Mark invoice Paid, create Payment
│   │   └── Update subscription status if applicable
│   │
│   ├── subscription.create → HandleSubscriptionCreateAsync
│   │   ├── Update local sub with PaystackSubscriptionCode
│   │   ├── Store email_token for future disable calls
│   │   └── Set status Active
│   │
│   ├── subscription.not_renew → HandleSubscriptionNotRenewAsync
│   │   └── Set status NonRenewing
│   │
│   ├── subscription.disable → HandleSubscriptionDisableAsync
│   │   └── Set status Cancelled
│   │
│   ├── subscription.expiring_cards → HandleExpiringCardsAsync
│   │   └── Send email to affected tenants with manage link
│   │
│   ├── invoice.create → HandleInvoiceCreateAsync
│   │   ├── Create local invoice (Draft)
│   │   └── Add subscription line items
│   │
│   ├── invoice.update → HandleInvoiceUpdateAsync
│   │   └── Update local invoice status
│   │
│   ├── invoice.payment_failed → HandleInvoicePaymentFailedAsync
│   │   ├── Mark invoice Overdue
│   │   └── Trigger DunningService.OnPaymentFailedAsync
│   │
│   ├── refund.pending → HandleRefundPendingAsync
│   ├── refund.processed → HandleRefundProcessedAsync
│   │   └── Update Payment.Status = Refunded, Invoice.Status = Refunded
│   ├── refund.failed → HandleRefundFailedAsync
│   │   └── Log, notify super admin
│   │
│   ├── charge.dispute.create → HandleDisputeCreateAsync
│   │   └── Log, notify super admin, flag tenant
│   └── charge.dispute.resolve → HandleDisputeResolveAsync
│
├── 5. Update WebhookEvent(Status = Processed, ProcessedAt = now)
│
└── 6. Return 200 OK

Error handling:
├── Catch exceptions → WebhookEvent(Status = Failed, ErrorMessage)
├── Still return 200 OK (to stop Paystack retries for unrecoverable errors)
└── Log error for investigation
```

---

## 8. Background Services

### 8.1 PlanSyncService (evolved)

- **Schedule**: Every 60 minutes
- **Change**: Sync both monthly AND annual plans to Paystack
- **Logic**: Call `IBillingService.SyncPlansAsync()`

### 8.2 SubscriptionSyncService (evolved)

- **Schedule**: Every 6 hours
- **Change**: Also process grace period expirations
- **Logic**:
  1. Call `IBillingService.ReconcileSubscriptionsAsync()`
  2. Call `DunningService.ProcessGracePeriodsAsync()`

### 8.3 UsageBillingJob (new)

- **Schedule**: Daily at 02:00 UTC
- **Logic**:
  1. Find all tenants whose current billing period ended today
  2. For each: call `UsageBillingService.ProcessEndOfPeriodAsync(tenantId)`
  3. Log summary of charges processed

### 8.4 DunningJob (new)

- **Schedule**: Every 4 hours
- **Logic**:
  1. Find subscriptions in `PastDue` status
  2. For each with last retry > `DunningIntervalHours` ago:
     - Call `DunningService.RetryChargeAsync(tenantId)`
  3. Find subscriptions past `GracePeriodEndsAt`:
     - Suspend tenant
  4. Log summary

### 8.5 DiscountExpiryJob (new)

- **Schedule**: Daily at 00:00 UTC
- **Logic**:
  1. Find TenantDiscounts where `ExpiresAt <= now` and `IsActive = true`
  2. Deactivate them
  3. Find Discounts where `ValidUntil <= now` and `IsActive = true`
  4. Deactivate them

### 8.6 InvoiceReminderJob (optional)

- **Schedule**: Daily at 10:00 UTC
- **Logic**:
  1. Find active subscriptions with `NextBillingDate` = 3 days from now
  2. Generate invoice preview
  3. Send reminder email with estimated amount

---

## 9. Tenant Billing Portal

### 9.1 Routes

| Route | View | Description |
|---|---|---|
| `GET /billing` | `Index` | Billing dashboard overview |
| `GET /billing/plans` | `Plans` | Plan selection with comparison |
| `POST /billing/subscribe` | — | Initialize subscription |
| `POST /billing/change-plan` | — | Change plan (with preview modal) |
| `GET /billing/change-plan/preview?planId=&cycle=` | JSON | AJAX preview of plan change |
| `GET /billing/seats` | `Seats` | Seat management (if PerSeatBilling enabled) |
| `POST /billing/seats` | — | Update seat count |
| `GET /billing/seats/preview?count=` | JSON | AJAX preview of seat change |
| `GET /billing/invoices` | `Invoices` | Invoice list |
| `GET /billing/invoices/{id}` | `InvoiceDetail` | Invoice with line items |
| `GET /billing/invoices/{id}/pdf` | PDF | Download invoice PDF |
| `GET /billing/usage` | `Usage` | Usage dashboard (if UsageBilling enabled) |
| `GET /billing/add-ons` | `AddOns` | Available add-ons (if AddOns enabled) |
| `POST /billing/add-ons/subscribe` | — | Subscribe to add-on |
| `POST /billing/add-ons/unsubscribe` | — | Unsubscribe from add-on |
| `GET /billing/profile` | `BillingProfile` | Company/billing details form |
| `POST /billing/profile` | — | Save billing profile |
| `POST /billing/discount` | — | Apply discount code |
| `GET /billing/payment-methods` | `PaymentMethods` | Saved cards + manage link |
| `POST /billing/cancel` | — | Cancel subscription |

### 9.2 Billing Dashboard View

```
┌─────────────────────────────────────────────────────────┐
│ Billing Overview                                         │
├─────────────────────┬───────────────────────────────────┤
│ Current Plan        │  Professional (Monthly)            │
│ Price               │  R499.00/month                     │
│ Status              │  ● Active                          │
│ Next Billing        │  March 1, 2026                     │
│ Seats               │  8 of 25 (17 available)            │
│ Credit Balance      │  R142.50                           │
├─────────────────────┴───────────────────────────────────┤
│ [Change Plan]  [Manage Seats]  [Update Card]             │
├─────────────────────────────────────────────────────────┤
│ Usage This Period (Feb 1 - Feb 28)                       │
│ ┌─────────────┬───────────┬───────────┬──────────────┐  │
│ │ Metric      │ Used      │ Included  │ Overage Cost │  │
│ ├─────────────┼───────────┼───────────┼──────────────┤  │
│ │ API Calls   │ 42,150    │ 50,000    │ R0.00        │  │
│ │ Storage     │ 35 GB     │ 50 GB     │ R0.00        │  │
│ └─────────────┴───────────┴───────────┴──────────────┘  │
├─────────────────────────────────────────────────────────┤
│ Active Discounts                                         │
│  LAUNCH50 — 50% off (2 cycles remaining)                 │
├─────────────────────────────────────────────────────────┤
│ Recent Invoices                                          │
│ ┌──────────────┬──────────┬────────┬─────────────────┐  │
│ │ Invoice      │ Amount   │ Status │ Date            │  │
│ ├──────────────┼──────────┼────────┼─────────────────┤  │
│ │ INV-2026-041 │ R249.50  │ Paid   │ Feb 1, 2026     │  │
│ │ INV-2026-032 │ R249.50  │ Paid   │ Jan 1, 2026     │  │
│ │ INV-2026-018 │ R499.00  │ Paid   │ Dec 1, 2025     │  │
│ └──────────────┴──────────┴────────┴─────────────────┘  │
│  [View All Invoices]                                     │
└─────────────────────────────────────────────────────────┘
```

### 9.3 Plan Selection View

```
┌────────────────────────────────────────────────────────────────────────────┐
│ Choose Your Plan                    [Monthly ◉]  [Annual ○ Save 17%]      │
├────────────────┬────────────────┬────────────────┬────────────────────────┤
│ Free           │ Starter        │ Professional   │ Enterprise             │
│ R0/mo          │ R199/mo        │ R499/mo        │ R999/mo                │
│                │ R1,990/yr      │ R4,990/yr      │ R9,990/yr              │
│ 3 users        │ 10 users       │ 25 users       │ Unlimited              │
│ 1,000 API/mo   │ 10,000 API/mo  │ 50,000 API/mo  │ Unlimited              │
│ 1 GB storage   │ 10 GB storage  │ 50 GB storage  │ Unlimited              │
│ ─────────────  │ ─────────────  │ ─────────────  │ ─────────────────────  │
│ ✓ Basic access │ ✓ Everything   │ ✓ Everything   │ ✓ Everything in Pro    │
│                │   in Free      │   in Starter   │ ✓ Priority support     │
│                │ ✓ Notes        │ ✓ Advanced     │ ✓ Custom integrations  │
│                │ ✓ Team sharing │   analytics    │ ✓ SLA guarantee        │
│                │                │ ✓ API access   │                        │
│ [Current Plan] │ [Subscribe]    │ [Subscribe]    │ [Contact Sales]        │
└────────────────┴────────────────┴────────────────┴────────────────────────┘
│ Have a discount code? [____________] [Apply]                               │
└────────────────────────────────────────────────────────────────────────────┘
```

### 9.4 Invoice Detail View

```
┌──────────────────────────────────────────────────────────┐
│ INVOICE                                                   │
│                                                           │
│ Your Company (Pty) Ltd          Invoice: INV-2026-00041   │
│ 123 Main Street                 Date: February 1, 2026    │
│ Cape Town, 8001                 Due: February 1, 2026     │
│ South Africa                    Status: PAID              │
│ VAT: 4123456789                 Period: Feb 1 - Feb 28    │
│                                                           │
│ Bill To:                                                  │
│ Acme Corp (Pty) Ltd                                       │
│ 456 Business Ave, Johannesburg                            │
│ VAT: 4987654321                                           │
│                                                           │
├────────────────────────────────┬─────┬────────┬──────────┤
│ Description                    │ Qty │ Price  │ Amount   │
├────────────────────────────────┼─────┼────────┼──────────┤
│ Professional Plan (Monthly)    │  1  │ R499   │  R499.00 │
│ Additional Seats (3 × R29)     │  3  │  R29   │   R87.00 │
│ Priority Support Add-On        │  1  │  R99   │   R99.00 │
│                                │     │        │          │
│ Subtotal                       │     │        │  R685.00 │
│ Discount (LAUNCH50 — 50% off)  │     │        │ -R342.50 │
│ VAT (15%)                      │     │        │   R51.38 │
│ Credit Applied                 │     │        │  -R44.00 │
│                                │     │        │──────────│
│ Total Due                      │     │        │  R349.88 │
├────────────────────────────────┴─────┴────────┴──────────┤
│ Payment: Visa ending 4242 — Paid Feb 1, 2026             │
│                                                           │
│ [Download PDF]  [Back to Invoices]                        │
└──────────────────────────────────────────────────────────┘
```

---

## 10. Super Admin Billing Management

### 10.1 Routes

| Route | Description |
|---|---|
| `GET /super-admin/plans` | Plan list (existing, enhanced) |
| `GET /super-admin/plans/{id}/edit` | Edit plan (enhanced with per-seat, setup fee, trial days) |
| `GET /super-admin/plans/{id}/pricing-tiers` | Manage tiered pricing |
| `GET /super-admin/discounts` | Discount code management |
| `GET /super-admin/discounts/create` | Create discount code |
| `GET /super-admin/discounts/{id}/edit` | Edit discount |
| `GET /super-admin/add-ons` | Add-on management |
| `GET /super-admin/add-ons/create` | Create add-on |
| `GET /super-admin/add-ons/{id}/edit` | Edit add-on |
| `GET /super-admin/tenants/{id}/billing` | Tenant billing admin |
| `POST /super-admin/tenants/{id}/billing/credit` | Manually add credit |
| `POST /super-admin/tenants/{id}/billing/charge` | Manually charge tenant |
| `POST /super-admin/tenants/{id}/billing/refund/{paymentId}` | Issue refund |
| `POST /super-admin/tenants/{id}/billing/change-plan` | Override plan |
| `GET /super-admin/billing/analytics` | Revenue analytics dashboard |
| `GET /super-admin/billing/webhooks` | Webhook event log |
| `GET /super-admin/billing/dunning` | Dunning queue |

### 10.2 Plan Editor (enhanced)

```
┌──────────────────────────────────────────────────────────┐
│ Edit Plan: Professional                                   │
├──────────────────────────────────────────────────────────┤
│ Name:        [Professional          ]                     │
│ Slug:        [professional          ] (readonly)          │
│ Description: [For growing teams     ]                     │
│                                                           │
│ ── Pricing ──                                             │
│ Billing Model: [Flat Rate ▼]                              │
│ Monthly Price: [499.00  ] ZAR                             │
│ Annual Price:  [4990.00 ] ZAR                             │
│                                                           │
│ ── Per-Seat (if PerSeat/Hybrid) ──                        │
│ Included Seats:       [5    ]                             │
│ Per Seat (Monthly):   [29.00]                             │
│ Per Seat (Annual):    [290.00]                            │
│ [Manage Pricing Tiers]                                    │
│                                                           │
│ ── Limits ──                                              │
│ Max Users:             [25   ] (blank = unlimited)        │
│ Max Requests/Min:      [120  ] (blank = unlimited)        │
│                                                           │
│ ── Registration ──                                        │
│ Setup Fee:             [0.00 ] (one-time)                 │
│ Trial Days:            [14   ] (blank = use global)       │
│                                                           │
│ ── Status ──                                              │
│ Active: [✓]                                               │
│ Sort Order: [2]                                           │
│                                                           │
│ Paystack Monthly Plan: PLN_abc123 (synced)                │
│ Paystack Annual Plan:  PLN_def456 (synced)                │
│                                                           │
│ [Save]  [Sync to Paystack]                                │
└──────────────────────────────────────────────────────────┘
```

### 10.3 Discount Management

```
┌──────────────────────────────────────────────────────────────────────┐
│ Discount Codes                                    [+ Create Code]    │
├──────────┬───────────────┬──────────┬────────┬───────────┬──────────┤
│ Code     │ Name          │ Type     │ Value  │ Redeemed  │ Status   │
├──────────┼───────────────┼──────────┼────────┼───────────┼──────────┤
│ LAUNCH50 │ Launch 50%    │ % off    │ 50%    │ 23/100    │ Active   │
│ WELCOME  │ Welcome R100  │ Fixed    │ R100   │ 156/∞     │ Active   │
│ SUMMER25 │ Summer Sale   │ % off    │ 25%    │ 45/50     │ Expiring │
│ BETA     │ Beta Testers  │ % off    │ 100%   │ 12/12     │ Full     │
└──────────┴───────────────┴──────────┴────────┴───────────┴──────────┘
```

### 10.4 Tenant Billing Admin

```
┌──────────────────────────────────────────────────────────┐
│ Tenant: Acme Corp — Billing Admin                         │
├──────────────────────────────────────────────────────────┤
│ Plan: Professional (Monthly)  Status: Active              │
│ Seats: 8/25  Credit Balance: R142.50                      │
│ Paystack Customer: CUS_abc123                             │
│ Authorization: AUTH_xyz789 (Visa ****4242)                │
│                                                           │
│ ── Actions ──                                             │
│ [Change Plan ▼] [Add Credit] [Manual Charge] [Suspend]   │
│                                                           │
│ ── Active Discounts ──                                    │
│ LAUNCH50 — 50% off (2 cycles left)  [Remove]              │
│ [Apply Discount Code: [________] [Apply]]                 │
│                                                           │
│ ── Invoices ──                                            │
│ INV-2026-041  R249.50  Paid      Feb 1   [View] [Refund] │
│ INV-2026-032  R249.50  Paid      Jan 1   [View] [Refund] │
│ INV-2026-018  R499.00  Paid      Dec 1   [View] [Refund] │
│                                                           │
│ ── Credit Ledger ──                                       │
│ +R200.00  Plan change credit     Jan 15   R142.50 remain  │
│ -R57.50   Applied to INV-041     Feb 1                    │
│                                                           │
│ ── Webhook Events ──                                      │
│ charge.success     Feb 1   Processed                      │
│ invoice.create     Jan 29  Processed                      │
│ subscription.create Jan 1  Processed                      │
└──────────────────────────────────────────────────────────┘
```

### 10.5 Revenue Analytics Dashboard

```
┌──────────────────────────────────────────────────────────┐
│ Billing Analytics                [This Month ▼]           │
├──────────────────────────────────────────────────────────┤
│                                                           │
│ MRR: R24,750      ARR: R297,000     Churn: 2.1%          │
│ Active Subs: 52   Trialing: 8       Past Due: 3          │
│                                                           │
│ ── Revenue by Plan ──                                     │
│ Free:          R0        (15 tenants)                     │
│ Starter:       R5,970    (30 tenants)                     │
│ Professional:  R11,475   (23 tenants — 3 annual)          │
│ Enterprise:    R7,305    (8 tenants — 5 annual)           │
│                                                           │
│ ── Revenue Breakdown ──                                   │
│ Subscriptions:  R22,450                                   │
│ Seat charges:   R1,160                                    │
│ Usage overages:  R340                                     │
│ Add-ons:        R792                                      │
│ Setup fees:     R500                                      │
│ Discounts:     -R2,430                                    │
│ Credits used:   -R62                                      │
│ Net Revenue:   R22,750                                    │
│                                                           │
│ ── Dunning Queue ──                                       │
│ 3 subscriptions in grace period                           │
│ Acme Corp — R499 — 2 retries left — [Retry Now]          │
│ Beta LLC — R199 — 1 retry left — [Retry Now]             │
│ Gamma Inc — R999 — Grace expires tomorrow — [Retry Now]  │
│                                                           │
│ ── Failed Webhooks (last 7 days) ──                       │
│ 0 failed events                                           │
└──────────────────────────────────────────────────────────┘
```

---

## 11. Database Migration

### New Tables

| Table | Primary Key | Notes |
|---|---|---|
| `PlanPricingTiers` | `Id` (Guid) | FK → Plans |
| `AddOns` | `Id` (Guid) | Standalone |
| `TenantAddOns` | `Id` (Guid) | FK → Tenants, AddOns |
| `InvoiceLineItems` | `Id` (Guid) | FK → Invoices |
| `Discounts` | `Id` (Guid) | Standalone |
| `TenantDiscounts` | `Id` (Guid) | FK → Tenants, Discounts |
| `TenantCredits` | `Id` (Guid) | FK → Tenants, Invoices? |
| `BillingProfiles` | `TenantId` (Guid) | 1:1 with Tenants |
| `WebhookEvents` | `Id` (Guid) | Standalone, unique index on Reference+EventType |

### Modified Tables

**Plans — Add Columns:**
- `BillingModel` (int, default 0 = FlatRate)
- `IncludedSeats` (int?, nullable)
- `PerSeatMonthlyPrice` (decimal?, nullable)
- `PerSeatAnnualPrice` (decimal?, nullable)
- `SetupFee` (decimal?, nullable)
- `TrialDays` (int?, nullable)
- `PaystackMonthlyPlanCode` (text, nullable)
- `PaystackAnnualPlanCode` (text, nullable)

**Plans — Remove Columns:**
- `PaystackPlanCode` (migrated to `PaystackMonthlyPlanCode`)

**Subscriptions — Add Columns:**
- `Quantity` (int, default 1)
- `TrialEndsAt` (datetime?, nullable)
- `GracePeriodEndsAt` (datetime?, nullable)
- `PaystackAuthorizationCode` (text, nullable)
- `PaystackEmailToken` (text, nullable)
- `PaystackAuthorizationEmail` (text, nullable)

**Invoices — Add Columns:**
- `Subtotal` (decimal, default 0)
- `DiscountAmount` (decimal, default 0)
- `TaxAmount` (decimal, default 0)
- `TaxRate` (decimal, default 0)
- `CreditApplied` (decimal, default 0)
- `Total` (decimal, default 0)
- `BillingPeriodStart` (datetime?, nullable)
- `BillingPeriodEnd` (datetime?, nullable)
- `CompanyName` (text, nullable)
- `CompanyAddress` (text, nullable)
- `CompanyVatNumber` (text, nullable)
- `TenantCompanyName` (text, nullable)
- `TenantBillingAddress` (text, nullable)
- `TenantVatNumber` (text, nullable)

**Invoices — Evolve:**
- `Amount` column remains as `Total` alias for compatibility

**Payments — Add:**
- `PartiallyRefunded` to `PaymentStatus` enum

**Tenants — Remove:**
- `TrialEndsAt` (moved to Subscription)

**Tenants — Add Navigation:**
- `BillingProfile`, `AddOns`, `Discounts`, `Credits` collections

### Indexes

```sql
-- WebhookEvent idempotency
CREATE UNIQUE INDEX IX_WebhookEvents_Reference_Type 
    ON WebhookEvents (PaystackReference, PaystackEventType);

-- Invoice lookup by tenant + period
CREATE INDEX IX_Invoices_TenantId_BillingPeriodStart 
    ON Invoices (TenantId, BillingPeriodStart);

-- Invoice number uniqueness
CREATE UNIQUE INDEX IX_Invoices_InvoiceNumber 
    ON Invoices (InvoiceNumber);

-- Active discounts per tenant
CREATE INDEX IX_TenantDiscounts_TenantId_IsActive 
    ON TenantDiscounts (TenantId, IsActive);

-- Unconsumed credits
CREATE INDEX IX_TenantCredits_TenantId_RemainingAmount 
    ON TenantCredits (TenantId) WHERE RemainingAmount > 0;

-- Usage records for billing period
CREATE INDEX IX_UsageRecords_TenantId_Metric_PeriodStart 
    ON UsageRecords (TenantId, Metric, PeriodStart);

-- Plan pricing tiers
CREATE INDEX IX_PlanPricingTiers_PlanId_MinUnits 
    ON PlanPricingTiers (PlanId, MinUnits);
```

### Migration Command

```bash
dotnet ef migrations add BillingEngineOverhaul --context CoreDbContext --output-dir Data/Core/Migrations
```

---

## 12. Testing Strategy

### Unit Tests

| Service | Test Cases | Count |
|---|---|---|
| **InvoiceEngine** | Line item generation, tax calculation (inclusive/exclusive), discount application, credit consumption, invoice numbering, company detail snapshot | ~12 |
| **CreditService** | Add credit, apply to invoice (partial/full), balance calculation, multiple credits consumed in order | ~8 |
| **DiscountService** | Code validation (active, expired, maxed, plan-restricted), percentage calculation, fixed amount (capped), stacking, cycle decrement | ~10 |
| **SeatBillingService** | Increase seats (prorated charge), decrease seats (credit), tiered pricing calculation, max seats validation | ~8 |
| **UsageBillingService** | Overage calculation (single metric, multiple), unlimited plan (no charge), zero overage, usage query by period | ~8 |
| **DunningService** | Payment failure handling, retry logic, grace period expiry, tenant suspension, reactivation | ~8 |
| **PaystackBillingService** | Plan sync (monthly + annual), subscription init (free/paid/trial), authorization storage, plan change (upgrade/downgrade), seat change, usage charge, refund, webhook idempotency | ~20 |
| **AddOnService** | Subscribe (one-off/recurring), unsubscribe with credit, list available/active | ~6 |
| **Total** | | **~80** |

### Integration Tests

| Scenario | Description |
|---|---|
| Full subscription lifecycle | Register → subscribe → invoice → pay → renew → cancel |
| Plan upgrade | Subscribe to Starter → upgrade to Pro → verify credit + new charge |
| Plan downgrade | Subscribe to Pro → downgrade to Starter → verify credit issued |
| Annual billing | Subscribe to annual plan → verify Paystack plan code used |
| Seat increase mid-cycle | Add 3 seats → verify prorated charge via authorization |
| Seat decrease mid-cycle | Remove 2 seats → verify credit issued |
| Usage billing | Record usage → trigger end-of-period → verify overage invoice |
| Discount flow | Apply code → subscribe → verify discounted invoice → next cycle discount applied → cycles exhausted |
| Dunning flow | Failed payment → grace period → retry success / suspension |
| Webhook idempotency | Send same webhook twice → verify processed once |
| One-off charge | Charge via saved authorization → verify invoice + payment |
| Refund | Full refund → verify payment status + invoice status |
| Add-on lifecycle | Subscribe to add-on → invoice → unsubscribe → credit |

### Manual Testing (Paystack Test Mode)

1. Create test plans in Paystack dashboard
2. Subscribe with test card `4084 0840 8408 4081` (Visa, always succeeds)
3. Test failed payments with `4084 0840 8408 4081` + expiry in the past
4. Verify webhook delivery in Paystack dashboard logs
5. Test manage link for card updates
6. Test refund processing
7. Test 2FA challenge flow on `charge_authorization`

---

## 13. Implementation Phases

### Phase 1: Foundation (Entity Model + Configuration)

**Estimated effort: 2–3 days**

1. Create `BillingOptions` class and update `appsettings.json`
2. Evolve `Plan` entity (add columns, remove `PaystackPlanCode`)
3. Evolve `Subscription` entity (add authorization fields, quantity, trial, grace)
4. Evolve `Invoice` entity (add line items, tax, company details)
5. Add `PartiallyRefunded` to `PaymentStatus`
6. Create `InvoiceLineItem` entity
7. Create `PlanPricingTier` entity
8. Create `AddOn` + `TenantAddOn` entities
9. Create `Discount` + `TenantDiscount` entities
10. Create `TenantCredit` entity
11. Create `BillingProfile` entity
12. Create `WebhookEvent` entity
13. Evolve `Tenant` entity (remove `TrialEndsAt`, add navigations)
14. Update `CoreDbContext` with all new DbSets + configurations
15. Generate EF Core migration
16. Update `CoreDataSeeder` (new plan fields)
17. Update all existing tests to compile with new entity shapes

### Phase 2: Core Services

**Estimated effort: 3–4 days**

1. Implement `CreditService`
2. Implement `DiscountService`
3. Implement `InvoiceEngine` (replaces `InvoiceGenerator`)
4. Implement `SeatBillingService`
5. Evolve `UsageMeteringService` → `UsageBillingService`
6. Implement `DunningService`
7. Implement `AddOnService`
8. Update `IBillingService` interface
9. Write unit tests for all new services

### Phase 3: Paystack Integration

**Estimated effort: 3–4 days**

1. Extend `PaystackClient` (charge_authorization, refunds, customer fetch)
2. Rewrite `PaystackBillingService`:
   - Plan sync (monthly + annual)
   - Subscription init (with trials, setup fees, metadata)
   - Authorization storage on charge.success
   - Plan change with credit system
   - Seat changes via charge_authorization
   - Usage billing via charge_authorization
   - One-off charges
   - Refunds
3. Webhook controller overhaul (idempotency, new event handlers)
4. Update `MockBillingService` to match new interface
5. Write PaystackClient unit tests
6. Write PaystackBillingService unit tests

### Phase 4: Background Services

**Estimated effort: 1–2 days**

1. Evolve `PlanSyncService` (monthly + annual)
2. Evolve `SubscriptionSyncService` (+ dunning integration)
3. Implement `UsageBillingJob`
4. Implement `DunningJob`
5. Implement `DiscountExpiryJob`
6. Optional: `InvoiceReminderJob`
7. Register all in DI + scheduling

### Phase 5: Tenant Portal

**Estimated effort: 3–4 days**

1. Billing dashboard view (overview, usage, discounts, recent invoices)
2. Plan selection view (monthly/annual toggle, feature comparison)
3. Seat management view (with AJAX preview)
4. Invoice list + detail view (with line items)
5. Invoice PDF generation
6. Billing profile form
7. Discount code input
8. Add-ons browser
9. Payment methods view
10. Controller actions for all routes

### Phase 6: Super Admin

**Estimated effort: 2–3 days**

1. Enhanced plan editor (per-seat, setup fee, trial, billing model)
2. Pricing tier management
3. Discount code CRUD
4. Add-on CRUD
5. Tenant billing admin (credit, charge, refund, plan override)
6. Revenue analytics dashboard
7. Webhook event viewer
8. Dunning queue viewer

### Phase 7: Testing & Polish

**Estimated effort: 2–3 days**

1. Integration tests for all billing flows
2. Manual Paystack test-mode testing
3. Email templates for billing events (payment receipt, payment failed, subscription cancelled, trial ending, invoice reminder)
4. Edge case handling (concurrent seat changes, webhook race conditions, zero-amount invoices)
5. Documentation updates

### Total Estimated Effort: 16–23 days

---

## 14. Architecture Decisions

### ADR-1: Paystack Subscriptions + charge_authorization Hybrid

**Decision**: Use Paystack subscriptions for the fixed recurring base plan amount. Use `charge_authorization` for all variable charges (seats, usage, one-off).

**Rationale**: Paystack subscriptions are rigid — fixed amount, fixed interval. They can't handle variable per-seat or usage charges. By storing the customer's `authorization_code` from the first successful payment, we can charge any amount at any time via the Charge Authorization API. The subscription handles the predictable base charge; our engine handles everything else.

**Consequences**: We must store and manage `authorization_code` securely. We must handle 2FA challenges on `charge_authorization` (some cards return `paused: true`). We own the retry logic for failed charges (Paystack doesn't retry subscriptions).

### ADR-2: Credit Ledger Over Instant Refunds for Proration

**Decision**: When a tenant downgrades, removes seats, or has unused days from a plan change, issue internal credits rather than Paystack monetary refunds.

**Rationale**: Paystack refunds can take up to 10 business days to process. They can fail and require manual intervention (`needs-attention` status). For routine proration, credits are instant, reliable, and automatically applied to the next invoice. Monetary refunds via Paystack are reserved for actual refund requests (customer wants money back).

**Consequences**: Tenants see a credit balance in their billing dashboard. Credits are consumed automatically on the next invoice. SuperAdmin can also manually issue credits.

### ADR-3: Two Paystack Plans Per Local Plan

**Decision**: Create separate Paystack plans for monthly and annual billing cycles of each local plan (e.g., "Professional (Monthly)" and "Professional (Annual)").

**Rationale**: Paystack plans are interval-specific — you can't have a single plan that supports both monthly and annual billing. The old system had a single `PaystackPlanCode` that only supported monthly. We need both to offer annual pricing.

**Consequences**: `SyncPlansAsync` creates/updates two plans per local plan. The `Plan` entity stores `PaystackMonthlyPlanCode` and `PaystackAnnualPlanCode` separately.

### ADR-4: Webhook Idempotency via WebhookEvent Table

**Decision**: Store every incoming webhook in a `WebhookEvent` table before processing. Check for duplicates by `PaystackReference + EventType`.

**Rationale**: Paystack retries webhooks if it doesn't receive a 200 OK response. Network issues or slow processing can cause duplicate deliveries. Without idempotency, we could double-charge, create duplicate invoices, or corrupt subscription state.

**Consequences**: Every webhook handler checks the `WebhookEvent` table first. Slightly more DB writes but complete safety against duplicates. Also provides an audit trail of all webhook events for debugging.

### ADR-5: SA VAT Handling (Inclusive by Default)

**Decision**: Default tax configuration is 15% VAT inclusive (prices shown include VAT). Configurable to exclusive via `Tax.Included = false`.

**Rationale**: Standard South African practice is VAT-inclusive pricing for consumer-facing products. However, B2B SaaS often shows exclusive pricing. Making it configurable supports both. When inclusive: the displayed R499 price includes R65.09 VAT. When exclusive: R499 + R74.85 VAT = R573.85 total.

**Tax calculation**:
- **Inclusive**: `taxAmount = total × taxRate / (1 + taxRate)` → `499 × 0.15 / 1.15 = R65.09`
- **Exclusive**: `taxAmount = subtotal × taxRate` → `499 × 0.15 = R74.85`

### ADR-6: Dunning Owned by Our Engine (Not Paystack)

**Decision**: Implement retry logic for failed payments ourselves using `charge_authorization` and background jobs.

**Rationale**: Paystack's documentation explicitly states: "Subscriptions are NOT retried on payment failure." When a subscription payment fails, Paystack fires the `invoice.payment_failed` webhook and moves on. We must own the dunning process:
1. Set grace period
2. Retry via `charge_authorization` at intervals
3. Escalate (emails → suspension) after max retries
4. Allow reactivation on catch-up payment

**Consequences**: Background `DunningJob` runs every 4 hours. `GracePeriodEndsAt` on subscription tracks deadline. Configurable via `GracePeriod` options.

### ADR-7: Invoice Line Items as Single Source of Truth

**Decision**: Every charge, discount, credit, and tax is represented as an `InvoiceLineItem` on the invoice.

**Rationale**: A single `Amount` field on an invoice is opaque. Customers can't understand what they're paying for. Support can't debug billing issues. Tax authorities require itemized invoices. Line items provide full transparency:
- `Subscription`: base plan charge
- `Seat`: per-seat charges
- `UsageCharge`: metered overage
- `Discount`: coupon reduction (negative)
- `Credit`: credit applied (negative)
- `Tax`: VAT/tax amount
- `SetupFee`: one-time fee

**Consequences**: `InvoiceEngine` assembles all line items before finalizing an invoice. PDF/HTML invoice renderers iterate line items. `Invoice.Total` is the computed sum.

### ADR-8: Per-App Configurability via appsettings.json

**Decision**: All billing features are toggled on/off via configuration. A fresh app using this starter kit can set `Features.PerSeatBilling = true` and the UI, services, and invoicing automatically include seat management.

**Feature toggles**:
- `AnnualBilling` — show annual pricing toggle
- `PerSeatBilling` — show seat management UI, include seat line items
- `UsageBilling` — track and charge for usage overages
- `AddOns` — show add-on marketplace
- `Discounts` — allow discount code input
- `SetupFees` — charge one-time fee on first subscription

**Rationale**: This starter kit will be used to build many different SaaS products. Some need per-seat pricing, others need usage billing, others just need flat-rate subscriptions. Each product should only expose the billing features it actually uses.

**Consequences**: All UI views check feature toggles before rendering sections. Services skip processing for disabled features. The default configuration is the simplest case: flat-rate monthly/annual subscriptions with discounts.

### ADR-9: `charge_authorization` 2FA Handling

**Decision**: When `charge_authorization` returns `paused: true`, redirect the user to the `authorization_url` to complete 2FA, then verify the transaction on callback.

**Rationale**: Some card issuers (especially in SA) require 2FA for recurring charges. Paystack handles this by returning a checkout URL instead of completing the charge. We cannot skip this — the charge won't process without user authorization.

**Consequences**: Any `charge_authorization` call site must handle the `paused` response. For background charges (usage billing, dunning retries), we email the tenant a payment link. For interactive charges (seat changes), we redirect in-browser.

### ADR-10: Amounts in Rands in DB, Kobo/Cents for Paystack API

**Decision**: Store all amounts as `decimal` in Rands (e.g., `499.00`) in the database. Convert to kobo (smallest currency unit, × 100) only when calling Paystack API.

**Rationale**: Humans think in Rands. The database, invoices, and UI all display Rands. Paystack's API requires the smallest currency unit (kobo for NGN, cents for ZAR). Conversion happens at the API boundary only.

**Conversion**: `amountInCents = (int)(amountInRands * 100)`
