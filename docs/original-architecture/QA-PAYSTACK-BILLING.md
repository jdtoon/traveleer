# QA Plan: Paystack Billing Integration

> **Scope**: End-to-end verification of the Paystack billing module against the live Paystack **test** environment.  
> **Pre-requisite**: A free Paystack account ([dashboard.paystack.co/#/signup](https://dashboard.paystack.co/#/signup)).

---

## Part 1 — Paystack Test Account Setup

### 1.1 Create a Paystack Account

1. Go to **https://dashboard.paystack.co/#/signup**
2. Sign up with your email (use a personal or dev email)
3. Verify your email — you land on the Dashboard in **Test Mode** by default
4. Confirm test mode: the toggle at the top-left should show **Test Mode** (orange badge)

### 1.2 Obtain Test API Keys

1. Navigate to **Settings → API Keys & Webhooks** (left sidebar)
2. Copy the **Test Secret Key** (`sk_test_...`) 
3. Copy the **Test Public Key** (`pk_test_...`)
4. You'll set the **Webhook URL** later (see §1.5)

### 1.3 Configure `appsettings.Development.json`

```json
{
  "Billing": {
    "Provider": "Paystack",
    "Paystack": {
      "SecretKey": "sk_test_YOUR_SECRET_KEY",
      "PublicKey": "pk_test_YOUR_PUBLIC_KEY",
      "WebhookSecret": "sk_test_YOUR_SECRET_KEY",
      "CallbackBaseUrl": "https://YOUR_NGROK_SUBDOMAIN.ngrok-free.app"
    }
  }
}
```

> **Important**: Paystack test mode uses your **Test Secret Key** as the webhook signing secret
> (the `x-paystack-signature` header is HMAC-SHA512 of the payload body signed with your secret key).
> So `WebhookSecret` = `SecretKey` in test mode.

### 1.4 Expose Localhost via ngrok

Paystack webhooks and callbacks must reach a public URL.

```bash
# Install ngrok: https://ngrok.com/download (free tier works)
ngrok http https://localhost:5001
```

This gives you a URL like `https://abc123.ngrok-free.app`.  
Update both:
- `appsettings.Development.json → Billing:Paystack:CallbackBaseUrl` = that URL
- Paystack Dashboard → Settings → API Keys & Webhooks → **Webhook URL** = `https://abc123.ngrok-free.app/api/webhooks/paystack`

### 1.5 Create Test Plans in Paystack

Our `PaystackPlanSyncService` auto-creates plans on startup, but you can also create them manually:

1. Dashboard → **Plans** → **Create Plan**
2. Create a **monthly** plan: name "Pro (Monthly)", amount ₦499 (or ZAR 499 if your account supports it), interval Monthly
3. Create an **annual** plan: name "Pro (Annual)", amount ₦4990, interval Annually
4. Note the **Plan Codes** (`PLN_xxxxx`)

If using auto-sync, just ensure your DB seed data has `MonthlyPrice > 0` for the Pro plan. The background service will create the plans in Paystack within 5 seconds of app startup.

### 1.6 Paystack Test Card Numbers

| Card Number | Type | CVV | Expiry | PIN | OTP | Behaviour |
|---|---|---|---|---|---|---|
| `4084 0840 8408 4081` | Visa | `408` | Any future date | — | — | **Success** (no validation, reusable) |
| `5078 5078 5078 5078 12` | Verve | `081` | Any future | `1111` | — | **Success** with PIN |
| `5060 6666 6666 6666 666` | Verve | `123` | Any future | `1234` | `123456` | **Success** with PIN+OTP |
| `4084 0840 8408 4081` | Visa (with `4081` ending) | `408` | Any future | — | — | Declined if amount ends in `08` |
| `5060 6666 6666 6666 666` | Verve | `123` | Any future | `0000` | — | **Declined** (wrong PIN) |
| `4084 0800 0000 5408` | Visa | `001` | Any future | — | — | **Declined** (insufficient funds) |

> **Amount rules for test**: Paystack test mode succeeds for most amounts. Transactions with amounts ending in `08` on the `4084` card are declined.

### 1.7 Start the Application

```bash
cd src
dotnet run
```

Verify in the console logs:
- `BillingModule registered with provider: Paystack`
- `Syncing plans with Paystack...`
- `Created Paystack plan PLN_xxxxx for Pro`

---

## Part 2 — End-to-End QA Test Cases

### TEST-01: Free Plan Registration (No Payment Required)

**Steps:**
1. Go to `https://localhost:5001/` → click "Get Started" or navigate to `/register`
2. Fill in: company name, slug, email, select **Free** plan
3. Submit the registration form

**Expected:**
- No redirect to Paystack — registration completes immediately
- Subscription record created in DB with `Status = Active`, `PaystackSubscriptionCode = NULL`
- Tenant provisioned, welcome email sent (check console logs for `ConsoleEmailService`)
- Navigating to `/{slug}/dashboard` works

**Verify (DB):**
```sql
SELECT * FROM Subscriptions WHERE TenantId = (SELECT Id FROM Tenants WHERE Slug = 'your-slug');
-- Status should be 0 (Active), PaystackSubscriptionCode should be NULL
```

---

### TEST-02: Paid Plan Registration (Full Paystack Checkout)

**Steps:**
1. Navigate to `/register`
2. Fill in: company name, slug, email, select **Pro** plan (Monthly)
3. Submit

**Expected:**
- Browser redirects to Paystack checkout page (`checkout.paystack.com`)
- URL should contain a reference and plan code

4. On Paystack checkout:
   - Enter email (same as registration)
   - Use test card: `4084 0840 8408 4081`, CVV `408`, expiry `12/30`
   - Click Pay

**Expected After Payment:**
- Paystack redirects to `https://YOUR_DOMAIN/register/callback?reference=xxx&trxref=xxx`
- Registration success page shown
- Tenant provisioned and active
- Welcome email sent

**Verify (DB):**
```sql
SELECT s.Status, s.PaystackSubscriptionCode, s.BillingCycle 
FROM Subscriptions s 
JOIN Tenants t ON s.TenantId = t.Id 
WHERE t.Slug = 'your-slug';
-- Status = Active, PaystackSubscriptionCode should have a reference
```

**Verify (Paystack Dashboard):**
- Transactions → you should see the test transaction with "success" status
- Subscriptions → a subscription should be created for this customer

---

### TEST-03: Webhook — charge.success

**Steps:**
1. Complete TEST-02 (a paid registration)
2. Check ngrok terminal or the app logs

**Expected (within seconds of payment):**
- App logs: `Processing Paystack webhook: charge.success`
- App logs: `Payment recorded for tenant xxx, reference xxx`
- Payment record created in DB

**Verify (DB):**
```sql
SELECT * FROM Payments WHERE TenantId = (SELECT Id FROM Tenants WHERE Slug = 'your-slug');
-- Should have a record with Status = Success, PaystackReference populated
```

---

### TEST-04: Webhook — subscription.create

**Expected (fires alongside charge.success for plan-on-transaction):**
- App logs: `Processing Paystack webhook: subscription.create`
- Subscription record updated with real `PaystackSubscriptionCode` (starts with `SUB_`)
- `PaystackCustomerCode` populated with customer code

**Verify (DB):**
```sql
SELECT PaystackSubscriptionCode, PaystackCustomerCode 
FROM Subscriptions 
WHERE TenantId = (SELECT Id FROM Tenants WHERE Slug = 'your-slug');
-- PaystackSubscriptionCode should start with SUB_, PaystackCustomerCode with CUS_
```

---

### TEST-05: Webhook Signature Validation

**Steps:**
1. Use `curl` to send a spoofed webhook **without** a valid signature:

```bash
curl -X POST https://YOUR_NGROK_URL/api/webhooks/paystack \
  -H "Content-Type: application/json" \
  -H "x-paystack-signature: invalid_signature_here" \
  -d '{"event":"charge.success","data":{"reference":"fake","amount":100}}'
```

**Expected:**
- App logs: `Invalid Paystack webhook signature`
- No payment or subscription records created from this call
- The endpoint still returns `200 OK` (we acknowledge even invalid signatures to prevent retries, though the signature check prevents processing)

2. Send without signature header at all:

```bash
curl -X POST https://YOUR_NGROK_URL/api/webhooks/paystack \
  -H "Content-Type: application/json" \
  -d '{"event":"charge.success","data":{"reference":"fake2","amount":100}}'
```

**Expected:**
- Returns `400 Bad Request` with "Missing signature"
- No data modified

---

### TEST-06: Callback URL with reference Parameter

**Steps:**
1. After TEST-02, note the reference from the subscription record
2. Manually navigate to: `https://localhost:5001/register/callback?reference=THE_REFERENCE`

**Expected:**
- If tenant already provisioned: shows success page (idempotent)
- If tenant still PendingSetup: provisions and shows success

3. Navigate without reference: `https://localhost:5001/register/callback`

**Expected:**
- Shows error: "Invalid payment reference. Please contact support."

4. Navigate with invalid reference: `https://localhost:5001/register/callback?reference=nonexistent`

**Expected:**
- Shows error: "Could not find your registration. Please contact support."

---

### TEST-07: Declined Card Payment

**Steps:**
1. Start a new paid registration (different slug)
2. On Paystack checkout, use declined card: `4084 0800 0000 5408`, CVV `001`, any future expiry
3. The card should be declined on the Paystack checkout page

**Expected:**
- Paystack shows "Transaction Failed" or "Declined"
- User is NOT redirected to callback
- Subscription in DB remains with the reference but no `charge.success` webhook fires
- Tenant stays in `PendingSetup` status

---

### TEST-08: PIN + OTP Card Flow

**Steps:**
1. Start a new paid registration
2. On Paystack checkout, use: `5060 6666 6666 6666 666`, CVV `123`, any future expiry
3. Enter PIN: `1234`
4. Enter OTP: `123456`

**Expected:**
- Multi-step authentication flow completes
- Payment succeeds
- Same result as TEST-02 (redirect to callback, tenant provisioned)

---

### TEST-09: Subscription Cancellation

**Pre-requisite:** A tenant with an active Paystack subscription (from TEST-02).

**Steps:**
1. Via the application UI or API, trigger subscription cancellation for the tenant
2. This calls `CancelSubscriptionAsync` which will:
   - Fetch the subscription from Paystack to get `email_token`
   - Call `POST /subscription/disable` with the subscription code and email token

**Expected:**
- App logs: `Fetching Paystack subscription: SUB_xxx`
- App logs: `Disabling Paystack subscription: SUB_xxx`
- Subscription status in DB becomes `Cancelled`, `CancelledAt` is set
- Audit entry created for the cancellation

**Verify (Paystack Dashboard):**
- Subscriptions → the subscription status should show as "Non-Renewing" or "Cancelled"

**Verify (DB):**
```sql
SELECT Status, CancelledAt FROM Subscriptions 
WHERE PaystackSubscriptionCode = 'SUB_xxx';
-- Status = Cancelled, CancelledAt is not null
```

---

### TEST-10: Plan Sync on Startup

**Steps:**
1. Delete any existing Paystack plan codes from the DB:
```sql
UPDATE Plans SET PaystackPlanCode = NULL WHERE MonthlyPrice > 0;
```
2. Restart the application
3. Watch the console logs

**Expected:**
- `Syncing plans with Paystack...`
- `Created Paystack plan PLN_xxx for Pro`
- `Plan sync complete. X plans processed.`

**Verify (DB):**
```sql
SELECT Name, PaystackPlanCode FROM Plans WHERE MonthlyPrice > 0;
-- PaystackPlanCode should now have PLN_ codes
```

**Verify (Paystack Dashboard):**
- Plans page should show the newly created plans

---

### TEST-11: Idempotent charge.success Processing

**Steps:**
1. After TEST-02, find the webhook payload that was processed
2. Replay the same `charge.success` webhook (Paystack occasionally resends):

```bash
# Create the same payload that Paystack sent (check app logs / ngrok inspector)
# Sign it properly with your test secret key, or...
# Simply wait — Paystack test mode sometimes sends duplicate webhooks
```

**Expected:**
- App logs: `Processing Paystack webhook: charge.success`
- But **no** duplicate Payment record (idempotency check on `PaystackReference`)
- Returns `200 OK`

**Verify (DB):**
```sql
SELECT COUNT(*) FROM Payments WHERE PaystackReference = 'the-reference';
-- Should be exactly 1, not 2
```

---

### TEST-12: Suspended Tenant Reactivation via charge.success

**Steps:**
1. Manually suspend a tenant:
```sql
UPDATE Tenants SET Status = 3 WHERE Slug = 'test-tenant'; -- 3 = Suspended
```
2. Process a `charge.success` webhook for that tenant (either via a new payment or by crafting a webhook):

```bash
# Using the ngrok inspector, replay the charge.success event
# Or trigger a new payment for the tenant
```

**Expected:**
- Tenant status changes from `Suspended` to `Active`
- Payment record created
- App logs: `Payment recorded for tenant xxx`

---

### TEST-13: invoice.create Webhook (Subscription Renewal)

> **Note**: This event fires 3 days before the next billing date. In test mode,
> you can create a subscription with a short interval or wait for the cycle.

**Manual Verification:**
Since invoice events fire on the billing cycle, manually test by crafting the webhook:

```bash
# Compute HMAC-SHA512 signature of the payload with your test secret key
# Or use the Paystack Dashboard → Webhooks → "Send Test Event" if available

PAYLOAD='{"event":"invoice.create","data":{"reference":"inv_test_001","amount":49900,"currency":"ZAR","subscription":{"subscription_code":"SUB_your_code"}}}'

# Compute signature (PowerShell):
$secret = "sk_test_YOUR_KEY"
$hmac = New-Object System.Security.Cryptography.HMACSHA512
$hmac.Key = [Text.Encoding]::UTF8.GetBytes($secret)
$hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($PAYLOAD))
$signature = [BitConverter]::ToString($hash).Replace("-","").ToLower()

curl -X POST https://YOUR_NGROK_URL/api/webhooks/paystack `
  -H "Content-Type: application/json" `
  -H "x-paystack-signature: $signature" `
  -d $PAYLOAD
```

**Expected:**
- Invoice record created in DB
- Invoice number follows `INV-{YEAR}-{NNNN}` format
- Invoice linked to the correct subscription via subscription_code lookup

---

### TEST-14: invoice.payment_failed Webhook

**Steps:**
1. Craft and send an `invoice.payment_failed` webhook for an active subscription:

```bash
PAYLOAD='{"event":"invoice.payment_failed","data":{"reference":"inv_fail_001","amount":49900,"subscription":{"subscription_code":"SUB_your_code"}}}'
# Sign and send as above
```

**Expected:**
- Subscription status changes to `PastDue`
- App logs: `Payment failed for subscription SUB_xxx, tenant xxx. Status set to PastDue.`

---

### TEST-15: invoice.update Webhook

**Steps:**
1. Create an invoice via TEST-13 first
2. Send an `invoice.update` webhook:

```bash
PAYLOAD='{"event":"invoice.update","data":{"reference":"inv_test_001","status":"success","amount":49900}}'
# Sign and send
```

**Expected:**
- Invoice status updated to `Paid`
- `PaidDate` set

---

### TEST-16: Webhook Rate Limiting

**Steps:**
1. Send 20+ rapid webhook requests:

```bash
for ($i = 0; $i -lt 25; $i++) {
    curl -s -o /dev/null -w "%{http_code}" -X POST https://YOUR_NGROK_URL/api/webhooks/paystack `
      -H "Content-Type: application/json" `
      -H "x-paystack-signature: fake" `
      -d '{"event":"test"}'
}
```

**Expected:**
- First N requests return `200` or `400` (depending on signature)
- After hitting the rate limit, requests return `429 Too Many Requests`
- Paystack will retry webhook delivery later (exponential backoff)

---

### TEST-17: Provider Switching (Paystack → Mock)

**Steps:**
1. Change `appsettings.Development.json`:
```json
{ "Billing": { "Provider": "Mock" } }
```
2. Restart the application

**Expected:**
- Console logs: `BillingModule registered with provider: Mock`
- No Paystack plan sync runs
- Registration flow uses `MockBillingService` (no real payment)
- All paths work without Paystack connectivity

---

### TEST-18: Plan Change (Upgrade)

**Pre-requisite:** A tenant on the Free plan.

**Steps:**
1. Trigger a plan change to Pro (via admin UI or API)
2. This calls `ChangePlanAsync`

**Expected:**
- For paid plans: returns a `PaymentUrl` for Paystack checkout
- After payment: tenant's plan updated
- For free→free: updates immediately, no payment URL

---

## Part 3 — Verification Checklist

### API Contract Compliance

| Paystack API | Our Implementation | Status |
|---|---|---|
| `POST /transaction/initialize` — `amount` in subunit (kobo/cents) | `plan.MonthlyPrice * 100` | ✅ |
| `POST /transaction/initialize` — `plan` parameter for auto-subscription | Passes `plan.PaystackPlanCode` | ✅ |
| `POST /transaction/initialize` — `callback_url` | Configurable via `CallbackBaseUrl` | ✅ |
| `POST /transaction/initialize` — `metadata` as JSON object | `Dictionary<string, object>` | ✅ |
| `GET /transaction/verify/:reference` — URL-encoded reference | `Uri.EscapeDataString(reference)` | ✅ |
| `POST /subscription/disable` — `code` + `token` (email_token) | Fetches subscription first to get email_token | ✅ Fixed |
| `POST /plan` — `amount` as integer in subunit | `(int)(price * 100)` | ✅ |
| Webhook signature — HMAC-SHA512 with secret key | `WebhookSignatureValidator` with constant-time comparison | ✅ |
| Always return 200 for valid webhooks | All handlers return `WebhookResult(true)`, controller returns `Ok()` | ✅ |

### Webhook Event Coverage

| Event | Handler | Verified |
|---|---|---|
| `charge.success` | ✅ Creates Payment, reactivates suspended tenants, idempotent | ☐ |
| `subscription.create` | ✅ Updates subscription code & customer code | ☐ |
| `subscription.not_renew` | ✅ Sets status to Cancelled | ☐ |
| `subscription.disable` | ✅ Sets status to Cancelled | ☐ |
| `invoice.create` | ✅ Creates Invoice (looks up by subscription code then metadata fallback) | ☐ |
| `invoice.update` | ✅ Updates Invoice status | ☐ |
| `invoice.payment_failed` | ✅ Sets subscription to PastDue | ☐ |

### Security Checklist

| Item | Status |
|---|---|
| Secret keys not committed to source control | ☐ Verify `appsettings.Development.json` is in `.gitignore` |
| Webhook signature verified before processing | ✅ `WebhookSignatureValidator` |
| Constant-time signature comparison (prevents timing attacks) | ✅ `CryptographicOperations.FixedTimeEquals` |
| Missing signature returns 400 (rejects unsigned requests) | ✅ |
| Rate limiting on webhook endpoint | ✅ `[EnableRateLimiting("webhook")]` |
| Secrets only in environment/config, never logged | ☐ Verify no secret key in logs |

---

## Part 4 — Known Limitations & Future Work

1. **`subscription.not_renew` semantics**: Currently sets status to `Cancelled` immediately. Paystack's intent is "will not renew on _next_ billing date" (still active now). Consider adding a `NonRenewing` status.

2. **`subscription.expiring_cards` webhook**: Not handled. Paystack sends this monthly for subscriptions with expiring cards. Not critical for MVP.

3. **Manage subscription link**: Paystack provides `GET /subscription/:code/manage/link` to generate a hosted page where customers can update their card or cancel. Not currently implemented.

4. **Subscription status sync**: We don't periodically poll Paystack to reconcile subscription statuses. If a webhook is missed, our DB could be stale. Consider adding a reconciliation job.

5. **Multi-currency**: Implementation defaults to ZAR. Plans should have proper currency and Paystack account must support the target currency.

---

## Part 5 — Quick Reference: Signature Computation (PowerShell)

For manually testing webhooks, compute the HMAC-SHA512 signature:

```powershell
function Get-PaystackSignature {
    param([string]$Payload, [string]$Secret)
    $hmac = New-Object System.Security.Cryptography.HMACSHA512
    $hmac.Key = [Text.Encoding]::UTF8.GetBytes($Secret)
    $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Payload))
    return [BitConverter]::ToString($hash).Replace("-", "").ToLower()
}

# Usage:
$payload = '{"event":"charge.success","data":{"reference":"test123","amount":49900,"currency":"ZAR","metadata":{"tenant_id":"YOUR-TENANT-GUID"}}}'
$sig = Get-PaystackSignature -Payload $payload -Secret "sk_test_YOUR_KEY"

Invoke-RestMethod -Method Post -Uri "https://YOUR_NGROK/api/webhooks/paystack" `
    -Headers @{ "x-paystack-signature" = $sig; "Content-Type" = "application/json" } `
    -Body $payload
```
