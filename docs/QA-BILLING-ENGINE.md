# Billing Engine — QA Testing Guide

> **Date**: 2026-02-23  
> **Scope**: Manual QA for the billing engine (Phases 1–7)  
> **Environments**: Mock billing (local dev) → Paystack test mode (Docker Compose local)  
> **Prerequisite**: All 272 automated tests passing (`dotnet test`)

---

## Table of Contents

1. [Environment Setup](#1-environment-setup)
2. [Part A: Mock Billing (Development)](#2-part-a-mock-billing-development)
3. [Part B: Paystack Test Mode (Docker Compose)](#3-part-b-paystack-test-mode-docker-compose)
4. [Feature Toggle Matrix](#4-feature-toggle-matrix)
5. [Test Cards & Paystack Test Data](#5-test-cards--paystack-test-data)
6. [Checklist Summary](#6-checklist-summary)

---

## 1. Environment Setup

### 1A. Mock Billing (local `dotnet run`)

```bash
cd src
dotnet run
```

Uses `appsettings.Development.json`:
- `Billing.Provider = "Mock"` — no real payment gateway, everything succeeds instantly
- `Billing.Features.PerSeatBilling = true` — seat management UI visible
- `Billing.Features.AddOns = true` — add-on section visible
- `Billing.Features.Discounts = true` — discount code input visible
- `Billing.Features.AnnualBilling = true` — annual toggle visible
- DevSeed creates a `demo` tenant with `admin@demo.local` on the `starter` plan

### 1B. Paystack Test Mode (Docker Compose local)

```bash
docker compose -f docker-compose.local.yml up --build
```

Uses environment variables in `docker-compose.local.yml`:
- `Billing__Provider=Paystack`
- Test keys (`sk_test_...`, `pk_test_...`)
- `Billing__Paystack__CallbackBaseUrl` must point to your ngrok/tunnel URL
- Paystack test mode accepts test card numbers (see Section 5)

**Required for Paystack webhooks:**
```bash
# Start ngrok tunnel (or similar) — needed for Paystack webhook callbacks
ngrok http 8080
# Update Billing__Paystack__CallbackBaseUrl and Site__BaseUrl with the ngrok URL
# Configure the ngrok URL + /api/paystack/webhook in Paystack dashboard → Settings → Webhooks
```

### 1C. Key URLs

| Page | URL |
|------|-----|
| Home / Marketing | `http://localhost:8080/` |
| Registration | `http://localhost:8080/register` |
| Tenant Billing | `http://localhost:8080/admin/billing` (logged in as tenant admin) |
| Super Admin | `http://localhost:8080/superadmin` (logged in as super admin) |
| Super Admin Billing | `http://localhost:8080/superadmin/discounts` |
| Super Admin Add-ons | `http://localhost:8080/superadmin/addons` |
| Super Admin Webhooks | `http://localhost:8080/superadmin/webhooks` |
| Hangfire Dashboard | `http://localhost:8080/superadmin/hangfire` |
| Paystack Webhook | `POST http://localhost:8080/api/paystack/webhook` |

---

## 2. Part A: Mock Billing (Development)

All tests in this section use `Billing.Provider = "Mock"`. No real payments are processed. The mock billing service auto-succeeds all operations and provisions tenants instantly.

### A1. Registration & Subscription Flow

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A1.1 | Register with free plan | 1. Go to `/register` 2. Fill form, select free/starter plan 3. Click Register 4. Check email, click verify link | Tenant created, redirected to workspace, status = Active | ☐ |
| A1.2 | Register with paid plan (mock) | 1. Go to `/register` 2. Fill form, select a paid plan 3. Click Register 4. Click verify link | No payment redirect (mock). Tenant provisioned immediately, status = Active | ☐ |
| A1.3 | Register with annual cycle | 1. Go to `/register` 2. Select paid plan, toggle to Annual billing 3. Complete registration | Subscription created with `BillingCycle = Annual`, correct annual price | ☐ |
| A1.4 | Registration with duplicate slug | 1. Register tenant "demo" 2. Try registering another tenant "demo" | Error: slug already taken | ☐ |
| A1.5 | Trial period set on free plan | 1. Register free plan tenant 2. Check DB: `Subscriptions` table | `TrialEndsAt` = now + 14 days | ☐ |

### A2. Billing Dashboard (Tenant Admin)

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A2.1 | View billing page | 1. Log in as tenant admin 2. Navigate to `/admin/billing` | Shows plan name, price, status, billing cycle, next billing date | ☐ |
| A2.2 | Plan details shown | On billing page | Current plan name, price, cycle displayed correctly | ☐ |
| A2.3 | Credit balance shown | On billing page | Credit balance displays (should be R0.00 initially) | ☐ |
| A2.4 | Invoice history shown | On billing page | Invoice table visible (may be empty for new tenant) | ☐ |
| A2.5 | Subscription status badge | On billing page | Status badge (Active/Trialing/etc.) with correct color | ☐ |

### A3. Plan Changes

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A3.1 | Preview plan upgrade | 1. Click "Change Plan" 2. Select higher-tier plan | Modal shows: current plan, new plan, prorated credit, amount due | ☐ |
| A3.2 | Preview plan downgrade | 1. Click "Change Plan" 2. Select lower-tier plan | Modal shows credit for unused period, R0.00 due (credit issued) | ☐ |
| A3.3 | Execute plan upgrade | 1. Preview upgrade 2. Confirm change | Plan changed, subscription updated, invoice created, credit applied | ☐ |
| A3.4 | Execute plan downgrade | 1. Preview downgrade 2. Confirm change | Plan changed, credit issued for unused period, new plan active at end of cycle | ☐ |
| A3.5 | Switch billing cycle (monthly → annual) | 1. Change plan with Annual cycle selected | Cycle updated, annual price applied | ☐ |
| A3.6 | Change to same plan | 1. Try changing to current plan | Error or no-op: "Already on this plan" | ☐ |

### A4. Seat Management (requires `Features.PerSeatBilling = true`)

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A4.1 | Seat section visible | On billing page when per-seat enabled | "Seats" section with current count + change button visible | ☐ |
| A4.2 | Preview add seats | 1. Click "Change Seats" 2. Enter higher number | Shows prorated charge for remaining days | ☐ |
| A4.3 | Preview remove seats | 1. Click "Change Seats" 2. Enter lower number | Shows credit to be issued | ☐ |
| A4.4 | Add seats | 1. Preview +2 seats 2. Confirm | Seats updated, proration invoice created, payment charged (mock auto-succeeds) | ☐ |
| A4.5 | Remove seats | 1. Preview −1 seat 2. Confirm | Seats reduced, credit issued for unused seat-days | ☐ |
| A4.6 | Exceed max seats | 1. Try setting seats > plan's MaxUsers | Error: "Exceeds maximum seats for this plan" | ☐ |
| A4.7 | Set seats to 0 | 1. Try setting seats = 0 | Error: "Seat count must be at least 1" | ☐ |
| A4.8 | Flat rate plan (no per-seat) | 1. On a FlatRate plan, try changing seats | Error: "Per-seat billing not available" or section hidden | ☐ |

### A5. Discounts (requires `Features.Discounts = true`)

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A5.1 | Apply valid discount code | 1. Enter valid code in discount input 2. Click Apply | Success message, discount name + value shown | ☐ |
| A5.2 | Apply invalid code | 1. Enter "INVALID123" 2. Click Apply | Error: "Discount code not found" | ☐ |
| A5.3 | Apply expired code | 1. Create discount with past ValidUntil (via SA) 2. Apply it | Error: "Discount has expired" | ☐ |
| A5.4 | Apply maxed-out code | 1. Create discount with MaxUses=1, apply once 2. Try applying again from another tenant | Error: "Discount has reached maximum uses" | ☐ |
| A5.5 | Apply plan-restricted code | 1. Create discount for plan X 2. Apply from tenant on plan Y | Error: "Discount not valid for your plan" | ☐ |
| A5.6 | Discount reflected on invoice | 1. Apply discount 2. Next invoice generated | Invoice has discount line item (negative amount) | ☐ |

### A6. Add-Ons (requires `Features.AddOns = true`)

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A6.1 | View add-ons page | 1. Navigate to add-ons section on billing page | Lists available add-ons with name, description, price | ☐ |
| A6.2 | Subscribe to monthly add-on | 1. Click Subscribe on a monthly add-on | Add-on activated, appears in "Active" list | ☐ |
| A6.3 | Subscribe to one-off add-on | 1. Click Subscribe on a one-off add-on | One-off invoice generated, add-on activated | ☐ |
| A6.4 | Unsubscribe from add-on | 1. Click Unsubscribe on an active add-on | Add-on deactivated, prorated credit issued for monthly remaining | ☐ |
| A6.5 | Cannot subscribe twice | 1. Subscribe to add-on 2. Try subscribing again | Error: "Already subscribed" or button disabled | ☐ |
| A6.6 | Inactive add-on not shown | 1. Deactivate add-on via Super Admin 2. Check tenant add-on list | Deactivated add-on not in available list | ☐ |

### A7. Invoices & Credits

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A7.1 | View invoice detail | 1. Click an invoice in the history list | Modal shows: invoice #, line items, tax, total, status, dates | ☐ |
| A7.2 | Invoice line items | In invoice detail | Has line items for: Subscription, Tax, Discount (if any), Credit (if any) | ☐ |
| A7.3 | Credit balance after plan downgrade | 1. Downgrade plan 2. Check billing page | Credit balance increased by prorated amount | ☐ |
| A7.4 | Credits auto-applied to next invoice | 1. Have credits from downgrade 2. Next invoice generates | Invoice.CreditApplied > 0, Total reduced | ☐ |

### A8. Subscription Cancellation

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A8.1 | Cancel subscription modal | 1. Click "Cancel Subscription" | Confirmation modal appears with tenant name input | ☐ |
| A8.2 | Cancel with wrong name | 1. Type incorrect name in confirmation | Error: name doesn't match | ☐ |
| A8.3 | Cancel subscription | 1. Type correct tenant name 2. Confirm | Subscription status = Cancelled, tenant status updated | ☐ |
| A8.4 | Manage subscription (Paystack link) | 1. Click "Manage Subscription" | With mock: may show "not available". With Paystack: redirects to Paystack manage URL | ☐ |

### A9. Super Admin — Discounts Management

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A9.1 | View discounts page | 1. Log in as super admin 2. Go to `/superadmin/discounts` | Table lists all discounts with code, type, value, status | ☐ |
| A9.2 | Create percentage discount | 1. Click "Add Discount" 2. Fill: Code="SAVE20", Type=Percentage, Value=20 3. Save | Discount created, appears in list | ☐ |
| A9.3 | Create fixed amount discount | 1. Add discount: Type=FixedAmount, Value=50 | Discount created with R50 value | ☐ |
| A9.4 | Create limited-use discount | 1. Add discount with MaxUses=10 | Discount shows 0/10 uses in list | ☐ |
| A9.5 | Create time-limited discount | 1. Add discount with ValidFrom=tomorrow, ValidUntil=next month | Discount shows valid date range | ☐ |
| A9.6 | Create plan-restricted discount | 1. Add discount with specific PlanId | Discount only valid for that plan | ☐ |
| A9.7 | Edit existing discount | 1. Click Edit on a discount 2. Change value 3. Save | Discount updated in list | ☐ |
| A9.8 | Deactivate discount | 1. Click Deactivate on a discount | Status changes to inactive, no longer usable by tenants | ☐ |
| A9.9 | Validation: duplicate code | 1. Create discount with existing code | Error: code already exists | ☐ |

### A10. Super Admin — Add-On Management

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A10.1 | View add-ons page | Go to `/superadmin/addons` | Table lists all add-ons with name, slug, price, interval, status | ☐ |
| A10.2 | Create monthly add-on | 1. Click "Add Add-On" 2. Fill form: name, slug, price, interval=Monthly 3. Save | Add-on created, appears in list | ☐ |
| A10.3 | Create one-off add-on | 1. Create add-on with interval=OneOff | Add-on created | ☐ |
| A10.4 | Create annual add-on | 1. Create add-on with interval=Annual | Add-on created | ☐ |
| A10.5 | Edit add-on | 1. Click Edit 2. Change price 3. Save | Add-on updated | ☐ |
| A10.6 | Deactivate add-on | 1. Edit add-on, set IsActive=false 2. Save | Add-on no longer shown to tenants | ☐ |

### A11. Super Admin — Tenant Billing Detail

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A11.1 | View tenant billing detail | Go to Super Admin → Tenants → click a tenant → Billing tab | Shows plan, subscription status, credit balance | ☐ |
| A11.2 | Add credit to tenant | 1. Enter amount + reason 2. Click "Add Credit" | Credit added, balance updated, audit logged | ☐ |
| A11.3 | Add credit validation | 1. Enter 0 or negative amount | Error or validation prevents submission | ☐ |
| A11.4 | View tenant invoices | In tenant billing detail | Invoice table with number, total, status, date | ☐ |
| A11.5 | View tenant payments | In tenant billing detail | Payment table with amount, status, method, date | ☐ |
| A11.6 | Refund payment (mock) | 1. Click "Refund" on a payment 2. Optionally enter partial amount 3. Confirm | Payment status updated, credit/refund issued | ☐ |
| A11.7 | View active discounts | In tenant billing detail | Active discount badges shown | ☐ |
| A11.8 | View active add-ons | In tenant billing detail | Active add-on badges shown | ☐ |
| A11.9 | View credit ledger | In tenant billing detail | Credit entries with reason, amount, date | ☐ |

### A12. Super Admin — Webhook Events

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A12.1 | View webhook events | Go to `/superadmin/webhooks` | Table of recent webhook events (may be empty with mock) | ☐ |
| A12.2 | Webhook detail | 1. Click "View" on a webhook event | Modal shows event type, reference, payload JSON, status, attempts | ☐ |

### A13. Background Jobs (Hangfire)

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A13.1 | Hangfire dashboard accessible | Go to `/superadmin/hangfire` | Hangfire dashboard loads, shows recurring jobs | ☐ |
| A13.2 | Dunning job registered | In Hangfire → Recurring Jobs | `DunningJob` listed, runs every hour | ☐ |
| A13.3 | Usage billing job registered | In Hangfire → Recurring Jobs | `UsageBillingJob` listed, runs daily at 1 AM | ☐ |
| A13.4 | Discount expiry job registered | In Hangfire → Recurring Jobs | `DiscountExpiryJob` listed, runs daily at 4 AM | ☐ |

### A14. Navigation & UI

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| A14.1 | Billing nav in tenant admin | Log in as tenant admin | "Billing" link visible in admin sidebar | ☐ |
| A14.2 | Billing section in SA sidebar | Log in as super admin | "Billing & Revenue", "Discounts", "Add-ons", "Webhooks" links visible | ☐ |
| A14.3 | Feature toggles hide UI | Set `Features.PerSeatBilling = false` | Seat management section hidden on billing page | ☐ |
| A14.4 | Feature toggles hide add-ons | Set `Features.AddOns = false` | Add-on section hidden on billing page | ☐ |
| A14.5 | Feature toggles hide discounts | Set `Features.Discounts = false` | Discount input hidden on billing page | ☐ |

---

## 3. Part B: Paystack Test Mode (Docker Compose)

These tests use the **real Paystack API** in **test mode**. You need:
- Docker Compose local running (`docker compose -f docker-compose.local.yml up --build`)
- ngrok tunnel active and configured in `Billing__Paystack__CallbackBaseUrl` + `Site__BaseUrl`
- Paystack test webhook URL configured in Paystack Dashboard → Settings → API Keys & Webhooks
- Test card numbers from Section 5

### B1. Registration with Paystack Payment

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B1.1 | Paid plan → Paystack redirect | 1. Register with paid plan 2. Verify email | Redirected to Paystack checkout page | ☐ |
| B1.2 | Complete payment on Paystack | 1. On Paystack checkout, enter test card 2. Complete payment | Redirected back to `/register/callback?reference=...`, tenant provisioned | ☐ |
| B1.3 | Callback verifies transaction | After payment redirect | App verifies reference with Paystack API, creates subscription + tenant | ☐ |
| B1.4 | Subscription created in DB | After successful payment | `Subscriptions` table: Status=Active, PaystackSubscriptionCode set | ☐ |
| B1.5 | Authorization stored | After successful payment | `BillingProfiles` table: PaystackAuthorizationCode, Last4, CardType populated | ☐ |
| B1.6 | Payment recorded | After successful payment | `Payments` table: entry with amount, reference, PaystackReference | ☐ |
| B1.7 | Invoice created | After successful payment | `Invoices` table: Status=Paid, line items for plan + tax | ☐ |
| B1.8 | Failed payment on Paystack | 1. Enter test card that fails 2. Payment fails | Redirected back with error, tenant stays PendingSetup, can retry | ☐ |
| B1.9 | Annual plan registration | 1. Register with annual cycle | Paystack checkout shows annual amount, subscription created with Annual cycle | ☐ |

### B2. Paystack Webhooks

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B2.1 | `charge.success` webhook | 1. Complete a payment 2. Check webhook events in SA | WebhookEvent recorded, payment linked, idempotent on replay | ☐ |
| B2.2 | `subscription.create` webhook | After subscription created via Paystack | Subscription synced, tenant activated | ☐ |
| B2.3 | `subscription.not_renew` webhook | 1. Cancel subscription from Paystack manage page | Subscription status → NonRenewing | ☐ |
| B2.4 | `subscription.disable` webhook | After subscription disabled in Paystack | Subscription status → Cancelled | ☐ |
| B2.5 | `invoice.payment_failed` webhook | 1. Use failing card / trigger from Paystack test panel | Subscription → PastDue, dunning email sent, grace period starts | ☐ |
| B2.6 | Webhook signature validation | 1. Send POST to `/api/paystack/webhook` with wrong signature | Returns 401 Unauthorized | ☐ |
| B2.7 | Webhook idempotency | 1. Replay same webhook payload twice | Second call is no-op (WebhookEvent already processed) | ☐ |
| B2.8 | Webhook events visible in SA | After webhooks received | `/superadmin/webhooks` shows all events with payload | ☐ |

### B3. Plan Changes with Paystack

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B3.1 | Preview plan upgrade | 1. On billing page, click Change Plan 2. Select higher plan | Shows prorated amounts correctly | ☐ |
| B3.2 | Execute plan upgrade | 1. Confirm plan change | charge_authorization called for prorated amount, new subscription created on Paystack | ☐ |
| B3.3 | Execute plan downgrade | 1. Change to cheaper plan | Credit issued for unused days, new Paystack subscription at lower amount | ☐ |
| B3.4 | Plan sync to Paystack | 1. Check Paystack dashboard → Plans | Monthly + Annual Paystack plans exist for each local plan | ☐ |

### B4. Seat Changes with Paystack

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B4.1 | Add seats → charge_authorization | 1. Add 2 seats 2. Confirm | Prorated charge via charge_authorization, seat count updated | ☐ |
| B4.2 | Remove seats → credit issued | 1. Remove 1 seat 2. Confirm | Credit issued for remaining seat-days, seat count reduced | ☐ |
| B4.3 | Seat change invoice created | After seat change | Proration invoice with seat line items | ☐ |

### B5. Discounts with Paystack

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B5.1 | Apply discount before payment | 1. Apply discount code 2. Next invoice | Discount reduces invoice amount, reflected in Paystack charge | ☐ |
| B5.2 | Duration discount decrements | 1. Apply "3 cycles" discount 2. After 1st invoice | RemainingBillingCycles reduced from 3 → 2 | ☐ |
| B5.3 | Forever discount persists | 1. Apply "forever" discount (no duration limit) | Applied on every invoice indefinitely | ☐ |

### B6. Add-Ons with Paystack

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B6.1 | Subscribe to one-off add-on | 1. Subscribe to one-off add-on | charge_authorization called, one-off invoice paid | ☐ |
| B6.2 | Monthly add-on on next invoice | 1. Subscribe to monthly add-on 2. Next subscription invoice | Add-on amount included in subscription invoice | ☐ |
| B6.3 | Unsubscribe → prorated credit | 1. Unsubscribe from monthly add-on | Credit issued for remaining days | ☐ |

### B7. Cancellation with Paystack

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B7.1 | Cancel subscription | 1. Go to billing 2. Cancel subscription | Paystack subscription disabled via API, local status = Cancelled | ☐ |
| B7.2 | Manage subscription link | 1. Click "Manage Subscription" | Redirected to Paystack customer portal / manage link | ☐ |

### B8. Dunning & Grace Period

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B8.1 | Payment failure → PastDue | 1. Trigger payment failure (via Paystack test tools) | Subscription → PastDue, GracePeriodEndsAt set (now + 3 days) | ☐ |
| B8.2 | Dunning email sent | After payment failure | Email sent to tenant contact with "Payment failed" message | ☐ |
| B8.3 | Dunning retry (charge_authorization) | 1. Dunning job fires (Hangfire) 2. If auth code exists | Retry charge attempted, if success → reactivate | ☐ |
| B8.4 | Grace period expired → suspend | 1. Set GracePeriodEndsAt to past 2. Run ProcessGracePeriodsAsync | Tenant status → Suspended | ☐ |
| B8.5 | Reactivation after payment | 1. Tenant makes catch-up payment | Subscription → Active, tenant → Active, GracePeriodEndsAt cleared | ☐ |

### B9. Refunds with Paystack

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B9.1 | Full refund via SA | 1. SA → Tenant Billing Detail 2. Click Refund on a payment | Paystack refund API called, payment status → Refunded | ☐ |
| B9.2 | Partial refund via SA | 1. Enter partial amount 2. Refund | Paystack partial refund, status → PartiallyRefunded | ☐ |
| B9.3 | Refund appears in Paystack dashboard | After refund | Visible in Paystack Dashboard → Transactions → Refunds | ☐ |

### B10. Invoice Generation & Tax

| # | Test Case | Steps | Expected Result | Pass |
|---|-----------|-------|-----------------|------|
| B10.1 | Tax-inclusive invoice | With `Tax.Included = true` | Invoice total = plan price, tax line item = `total × rate / (1 + rate)` | ☐ |
| B10.2 | Tax-exclusive invoice | Change to `Tax.Included = false` | Invoice total = plan price + tax, tax = `subtotal × rate` | ☐ |
| B10.3 | Invoice number sequence | After multiple invoices | Sequential: INV-0001, INV-0002, etc. | ☐ |
| B10.4 | Invoice shows company info | In invoice detail | Company name + address from `Billing.Company` config | ☐ |
| B10.5 | Credit applied to invoice | 1. Have R50 credit 2. R100 invoice generated | Invoice shows CreditApplied = R50, Total = R50, credit line item | ☐ |

---

## 4. Feature Toggle Matrix

Test that toggling features on/off changes the UI appropriately. Restart the app after changing config.

| Feature | Config Key | When OFF | When ON |
|---------|-----------|----------|---------|
| Annual Billing | `Billing__Features__AnnualBilling` | No monthly/annual toggle on registration or billing page | Toggle visible, can choose cycle |
| Per-Seat Billing | `Billing__Features__PerSeatBilling` | No seat management section on billing page | Seat count display + "Change Seats" button |
| Usage Billing | `Billing__Features__UsageBilling` | No usage metrics display | Usage section with metric breakdowns |
| Add-Ons | `Billing__Features__AddOns` | No add-on section on billing page | Available/active add-ons section |
| Discounts | `Billing__Features__Discounts` | No discount code input | Discount code input + apply button |
| Setup Fees | `Billing__Features__SetupFees` | No setup fee on first invoice | Setup fee line item on initial subscription |

---

## 5. Test Cards & Paystack Test Data

### Paystack Test Cards

| Card Number | Behavior |
|-------------|----------|
| `4084 0840 8408 4081` | Successful charge (Visa) |
| `5060 6666 6666 6666 666` | Successful charge (Verve) |
| `5078 5078 5078 5078 12` | Failed transaction — insufficient funds |
| `4084 0840 8408 4082` | Request for PIN |
| `5060 6666 6666 6666 667` | Request for OTP |

> Use **any future expiry date** (e.g., 12/30), **any 3-digit CVV**, and **any PIN** (e.g., 1234).

### Paystack Test Emails

Use any email address. Paystack test mode doesn't send real emails.

### Paystack Webhook Testing

You can trigger test webhooks from the Paystack Dashboard:
1. Go to **Settings → Webhooks** in your Paystack test dashboard
2. Use the **"Test" button** to send test webhook events
3. Or use the Paystack CLI: `paystack webhook test --event charge.success`

### Key Paystack Event Types

| Event | When Fired |
|-------|-----------|
| `charge.success` | Any successful charge (subscription or one-off) |
| `subscription.create` | New subscription created |
| `subscription.not_renew` | Subscription set to not renew |
| `subscription.disable` | Subscription cancelled/disabled |
| `invoice.create` | Paystack subscription invoice created |
| `invoice.payment_failed` | Subscription renewal payment failed |
| `refund.processed` | Refund completed |
| `transfer.success` | Transfer completed |

---

## 6. Checklist Summary

### Part A: Mock Billing — Quick Totals

| Section | Tests | Passed |
|---------|-------|--------|
| A1. Registration | 5 | /5 |
| A2. Billing Dashboard | 5 | /5 |
| A3. Plan Changes | 6 | /6 |
| A4. Seat Management | 8 | /8 |
| A5. Discounts | 6 | /6 |
| A6. Add-Ons | 6 | /6 |
| A7. Invoices & Credits | 4 | /4 |
| A8. Cancellation | 4 | /4 |
| A9. SA Discounts | 9 | /9 |
| A10. SA Add-Ons | 6 | /6 |
| A11. SA Tenant Billing | 9 | /9 |
| A12. SA Webhooks | 2 | /2 |
| A13. Background Jobs | 4 | /4 |
| A14. Navigation & UI | 5 | /5 |
| **Part A Total** | **79** | **/79** |

### Part B: Paystack Test Mode — Quick Totals

| Section | Tests | Passed |
|---------|-------|--------|
| B1. Registration + Payment | 9 | /9 |
| B2. Webhooks | 8 | /8 |
| B3. Plan Changes | 4 | /4 |
| B4. Seat Changes | 3 | /3 |
| B5. Discounts | 3 | /3 |
| B6. Add-Ons | 3 | /3 |
| B7. Cancellation | 2 | /2 |
| B8. Dunning & Grace | 5 | /5 |
| B9. Refunds | 3 | /3 |
| B10. Invoice & Tax | 5 | /5 |
| **Part B Total** | **45** | **/45** |

### Grand Total: **124 manual test cases**

---

## Notes

- **Order of testing**: Complete Part A (Mock) first to validate all UI flows without payment complexity. Then Part B (Paystack) to test real gateway integration.
- **Database inspection**: Use the SQLite viewer or EF Core queries to verify data when the UI doesn't expose it (e.g., authorization codes, webhook events).
- **Seq logs**: Check structured logs in Seq (`http://localhost:8081`) for detailed billing operation traces.
- **Hangfire**: For dunning/grace period tests, you can manually trigger jobs from the Hangfire dashboard rather than waiting for the schedule.
- **Clean state**: To start fresh, stop Docker Compose and remove the `app-data` volume: `docker compose -f docker-compose.local.yml down -v`
