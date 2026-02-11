# Testing Third-Party Integrations

Step-by-step guide for obtaining credentials and testing each external service locally before deploying to production.

> **Prerequisite**: The app runs locally via `dotnet run` from `src/` or via `docker compose up --build`. All providers default to mocks in Development — this guide covers switching to real providers one at a time.

---

## Table of Contents

1. [Paystack (Billing)](#1-paystack-billing)
2. [AWS SES (Email)](#2-aws-ses-email)
3. [Cloudflare Turnstile (Bot Protection)](#3-cloudflare-turnstile-bot-protection)
4. [Cloudflare R2 (Storage)](#4-cloudflare-r2-storage)
5. [Litestream (Database Backup)](#5-litestream-database-backup)
6. [Testing Order & Tips](#6-testing-order--tips)

---

## 1. Paystack (Billing)

### Get Your Keys

1. Sign up at [dashboard.paystack.com](https://dashboard.paystack.com)
2. Go to **Settings → API Keys & Webhooks**
3. Copy your **Test** keys (they start with `sk_test_` / `pk_test_`):
   - **Secret Key** (`sk_test_...`) — server-side API calls
   - **Public Key** (`pk_test_...`) — frontend checkout
4. Under **Webhooks**, note the **Webhook Secret** — used to verify incoming webhook signatures

> **Important**: Use **test keys** for local development. Test mode lets you use Paystack's test cards without real charges.

### Configure Locally

Set these environment variables (or add to `appsettings.Development.json`):

```bash
# PowerShell
$env:Billing__Provider = "Paystack"
$env:Billing__Paystack__SecretKey = "sk_test_xxxxx"
$env:Billing__Paystack__PublicKey = "pk_test_xxxxx"
$env:Billing__Paystack__WebhookSecret = "your_webhook_secret"
$env:Billing__Paystack__CallbackBaseUrl = "https://your-ngrok-url.ngrok-free.app"
```

Or add to `src/appsettings.Development.json`:

```json
{
  "Billing": {
    "Provider": "Paystack",
    "Paystack": {
      "SecretKey": "sk_test_xxxxx",
      "PublicKey": "pk_test_xxxxx",
      "WebhookSecret": "your_webhook_secret",
      "CallbackBaseUrl": "https://your-ngrok-url.ngrok-free.app"
    }
  }
}
```

### Expose Local Server for Webhooks

Paystack needs to reach your local server for webhooks and callbacks. Use [ngrok](https://ngrok.com):

```bash
# Install ngrok (https://ngrok.com/download) then:
ngrok http 5001    # or 8080 if using Docker
```

Copy the `https://xxxx.ngrok-free.app` URL and:
1. Set `Billing__Paystack__CallbackBaseUrl` to this URL
2. Go to Paystack Dashboard → **Settings → API Keys & Webhooks**
3. Set **Webhook URL** to: `https://xxxx.ngrok-free.app/api/webhooks/paystack`

### What Happens on Startup

When `Billing:Provider=Paystack`:
- **PaystackPlanSyncService** runs after 5 seconds — syncs all active paid plans to Paystack. Each plan gets a `PaystackPlanCode` stored in the database.
- **PaystackSubscriptionSyncService** runs after 30 seconds — reconciles subscription statuses every 6 hours.
- Check logs for `[Paystack]` entries to confirm sync succeeded.

### Test Scenarios

#### A. Plan Sync
1. Start the app with Paystack provider
2. Check logs — you should see plans being created on Paystack
3. Verify in Paystack Dashboard → **Products → Plans** — you should see "Starter (Monthly)", "Professional (Monthly)", "Enterprise (Monthly)"

#### B. Registration with Payment
1. Go to `/register`, select a paid plan (e.g. Starter)
2. Fill in the form, submit
3. You'll be redirected to Paystack checkout
4. Use test card: `4084 0840 8408 4081`, expiry: any future date, CVV: `408`, OTP: `123456`
5. After payment, Paystack redirects to `/register/callback?reference=...`
6. The app verifies the transaction, provisions the tenant, sends welcome email

#### C. Plan Change
1. Log into a tenant's admin → Billing
2. Click "Change Plan" → select a different plan
3. Preview shows prorated amounts
4. Confirm — if upgrading, redirects to Paystack for the prorated charge
5. After payment, subscription updates to the new plan

#### D. Subscription Cancel
1. In Billing admin, click "Cancel"
2. Confirm the browser dialog
3. Subscription status changes to `Cancelled`
4. On Paystack side, the subscription is disabled

#### E. Webhooks
With ngrok running, Paystack will send real-time events:
- `charge.success` — after successful payment
- `subscription.create` — when new subscription is created
- `invoice.create` — when invoice is generated
- `invoice.payment_failed` — test by using a declining card

Check app logs for `[Paystack Webhook]` entries.

### Paystack Test Cards

| Card Number | Type | Result |
|---|---|---|
| `4084 0840 8408 4081` | Visa | Success (OTP: `123456`) |
| `5060 6666 6666 6666 666` | Verve | Success |
| `4084 0840 8408 4082` | Visa | Declined |

Full list: [Paystack Test Cards Documentation](https://paystack.com/docs/payments/test-payments/)

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Plans not syncing | Invalid secret key | Check `sk_test_` key is correct |
| Webhook 401/403 | Wrong webhook secret | Match the secret in Paystack dashboard |
| Callback 404 | ngrok URL changed | Update `CallbackBaseUrl` and Paystack webhook URL |
| "Plan not found" on checkout | Plan sync hasn't run | Wait 5s after startup or restart the app |
| Redirect loop on register | `CallbackBaseUrl` points to localhost | Must use ngrok or public URL |

---

## 2. AWS SES (Email)

### Get Your Credentials

1. Sign in to [AWS Console](https://console.aws.amazon.com)
2. Go to **IAM → Users → Create User**
3. Create a user with programmatic access
4. Attach the policy `AmazonSESFullAccess` (or a scoped policy for `ses:SendEmail`)
5. Save the **Access Key ID** and **Secret Access Key**

#### Verify Sender Email
SES requires you to verify the "from" address (or entire domain):

1. Go to **Amazon SES → Verified identities**
2. Click **Create identity** → choose **Email address**
3. Enter the email you'll use as `FromAddress` (e.g. `noreply@yourdomain.com`)
4. Check inbox and click the verification link

> **Sandbox Mode**: New SES accounts are in sandbox — you can only send to verified addresses. Request production access via AWS support to send to any address.

#### Choose a Region
Pick a region close to your users:
- `af-south-1` (Cape Town) — good for South African SaaS
- `eu-west-1` (Ireland) — European fallback
- `us-east-1` (Virginia) — default

### Configure Locally

```bash
$env:Email__Provider = "SES"
$env:Email__FromAddress = "noreply@yourdomain.com"
$env:Email__SES__AccessKey = "AKIA..."
$env:Email__SES__SecretKey = "your-secret-key"
$env:Email__SES__Region = "af-south-1"
```

### Test Scenarios

#### A. Magic Link Login
1. Start the app with SES provider
2. Navigate to a tenant login page (e.g. `/demo/login`)
3. Enter an email address (must be verified if in SES sandbox)
4. Click "Send Magic Link"
5. Check inbox — you should receive an email with a sign-in link
6. Click the link to log in

#### B. Welcome Email (Registration)
1. Register a new tenant (Free plan to avoid Paystack dependency)
2. After successful registration, a welcome email is sent to the admin email
3. Check inbox for the welcome message

#### C. Contact Form
1. Navigate to `/contact`
2. Submit the form
3. Email is sent to the `SupportEmail` configured in `Site` settings

### Emails the System Sends

| Email | Trigger | Recipient |
|---|---|---|
| Magic link | Login request | User requesting login |
| Welcome email | Tenant registration | Tenant admin |
| Expiring card | Paystack `subscription.expiring_cards` webhook | Tenant admin |
| Contact form | Contact form submission | `Site:SupportEmail` |

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `MessageRejected` error | From address not verified | Verify the `FromAddress` in SES console |
| Emails not received | SES sandbox mode | Can only send to verified addresses; request production access |
| `InvalidClientTokenId` | Wrong access key | Check IAM credentials |
| `SignatureDoesNotMatch` | Wrong secret key | Regenerate credentials in IAM |
| `Endpoint not found` | Wrong region | Check the `Region` value matches your SES setup |

---

## 3. Cloudflare Turnstile (Bot Protection)

### Get Your Keys

1. Sign in to [Cloudflare Dashboard](https://dash.cloudflare.com)
2. Go to **Turnstile** (left sidebar, under "Security")
3. Click **Add Site**
4. Configure:
   - **Site Name**: anything descriptive
   - **Domain**: `localhost` (for local testing)
   - **Widget Type**: "Managed" (recommended)
5. Copy the **Site Key** and **Secret Key**

> **Tip**: Adding `localhost` as a domain lets you test locally without any tunnel. Your production domain should also be added.

### Configure Locally

```bash
$env:Turnstile__Provider = "Cloudflare"
$env:Turnstile__SiteKey = "0x4AAAA..."
$env:Turnstile__SecretKey = "0x4AAAA..."
```

### Where Turnstile Appears

The widget appears on three forms (only when `Provider=Cloudflare`):

| Page | URL | Purpose |
|---|---|---|
| Registration | `/register` | Prevents bot signups |
| Contact form | `/contact` | Prevents spam submissions |
| Tenant login | `/{slug}/login` | Prevents brute-force login attempts |

When `Provider=Mock`, a hidden input with `"mock-captcha-token"` is used instead.

### Test Scenarios

1. **Registration page**: Go to `/register`. The Turnstile widget should render (a small checkbox/interactive challenge). Complete it, then submit the form. Validation passes server-side.

2. **Contact form**: Go to `/contact`. Widget should appear. Submit with and without completing the challenge.

3. **Login page**: Go to `/demo/login`. Widget should appear below the email field.

4. **Failure case**: Open browser dev tools, modify the hidden `cf-turnstile-response` value to garbage. Submit — server-side validation should reject with an error.

### Cloudflare Test Keys (Alternative)

Cloudflare provides test keys that always pass/fail — useful for CI:

| Type | Site Key | Secret Key |
|---|---|---|
| Always passes | `1x00000000000000000000AA` | `1x0000000000000000000000000000000AA` |
| Always fails | `2x00000000000000000000AB` | `2x0000000000000000000000000000000AB` |
| Forces challenge | `3x00000000000000000000FF` | `3x0000000000000000000000000000000FF` |

Docs: [Cloudflare Turnstile Testing](https://developers.cloudflare.com/turnstile/troubleshooting/testing/)

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Widget doesn't appear | Wrong provider setting | Ensure `Turnstile__Provider=Cloudflare` |
| Widget shows error | Domain not allowed | Add `localhost` to your Turnstile site config |
| Server-side validation fails | Wrong secret key | Check the secret key in Cloudflare dashboard |
| Widget renders but form still rejected | Token expired | Turnstile tokens expire after 300s; re-complete the challenge |

---

## 4. Cloudflare R2 (Storage)

### Get Your Credentials

1. Sign in to [Cloudflare Dashboard](https://dash.cloudflare.com)
2. Go to **R2 Object Storage** (left sidebar)
3. **Create a bucket**:
   - Name: `your-saas-storage` (or whatever you prefer)
   - Region: "Automatic" or choose one
4. Go to **R2 → Overview → Manage R2 API Tokens**
5. Click **Create API Token**:
   - Permission: **Object Read & Write**
   - Specify bucket: select your bucket
6. Copy the **Access Key ID**, **Secret Access Key**, and **Endpoint URL**

The endpoint URL looks like: `https://<account-id>.r2.cloudflarestorage.com`

#### Optional: Custom Public Domain
If you want public access to uploaded files:
1. In your bucket settings, enable **Public Access**
2. Connect a custom domain (e.g. `cdn.yourdomain.com`)
3. Use this as `R2PublicUrl`

### Configure Locally

```bash
$env:Storage__Provider = "R2"
$env:Storage__R2Bucket = "your-saas-storage"
$env:Storage__R2Endpoint = "https://xxxx.r2.cloudflarestorage.com"
$env:Storage__R2AccessKey = "your-access-key"
$env:Storage__R2SecretKey = "your-secret-key"
$env:Storage__R2PublicUrl = "https://cdn.yourdomain.com"  # optional
```

### Test Scenarios

R2 storage is registered as infrastructure (`IStorageService`) for generic blob storage. It's available for file upload features. To verify the connection:

1. Start the app with R2 provider — if credentials are wrong, the app throws `InvalidOperationException` on startup
2. A successful startup confirms the R2 client was initialized correctly
3. You can verify bucket access by checking R2 dashboard for any test uploads

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Startup crash with `InvalidOperationException` | Missing R2 credentials | Set all `Storage__R2*` env vars |
| `AccessDenied` error | Incorrect API token permissions | Ensure "Object Read & Write" for the bucket |
| Upload works but URL 403 | Public access not enabled | Enable public access on the R2 bucket |

---

## 5. Litestream (Database Backup)

### Get R2 Credentials for Backup

Litestream backs up SQLite databases to Cloudflare R2. You need a **separate R2 bucket** (or the same one with a prefix):

1. In Cloudflare R2, create a bucket: `saas-backups`
2. Create an API token with **Object Read & Write** for this bucket
3. Note the Access Key, Secret Key, and Endpoint URL

### Configure Locally (Docker Only)

Litestream runs as a **Docker sidecar** — it's not used with `dotnet run`. To test:

```bash
# Create .env in the project root
cat > .env <<EOF
R2_ACCESS_KEY_ID=your-access-key
R2_SECRET_ACCESS_KEY=your-secret-key
R2_ENDPOINT=https://xxxx.r2.cloudflarestorage.com
R2_BUCKET=saas-backups
EOF
```

Then run production Docker (without the dev override):

```bash
docker compose -f docker-compose.yml up --build
```

### How Backup Works

1. **App starts** → `LitestreamConfigSyncService` generates `/app/db/litestream.yml` listing `core.db`, `audit.db`, and all tenant `*.db` files
2. **Litestream sidecar** starts after app is healthy → reads the config → begins continuous WAL replication to R2
3. **New tenants** → config sync runs every 5 minutes, regenerates YAML, writes sentinel file → sidecar auto-reloads
4. **Databases backed up**: `core.db`, `audit.db`, and every `tenants/*.db`

### Test Scenarios

1. Start with `docker compose -f docker-compose.yml up --build`
2. Check litestream container logs: `docker compose logs litestream`
3. You should see replication starting for each database
4. Register a tenant → within 5 minutes the new tenant DB appears in the backup config
5. Check R2 bucket — you should see WAL segments being uploaded

### Verify Restore

```bash
# Stop containers
docker compose -f docker-compose.yml down

# Remove the data volume
docker volume rm saas_app-data

# Restart — litestream would need to restore before app starts
# (Note: automatic restore is not configured in the current entrypoint;
#  manual restore: litestream restore -config /app/db/litestream.yml /app/db/core.db)
```

### Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Litestream exits immediately | Config file not yet generated | App health check must pass first; check app logs |
| "access denied" in litestream logs | Wrong R2 credentials | Check `.env` values |
| New tenant DB not backed up | Config sync hasn't run yet | Wait up to 5 minutes or restart app |
| Sidecar not reloading | Sentinel file permission issue | Check volume permissions |

---

## 6. Testing Order & Tips

### Recommended Testing Order

Test each integration independently, one at a time:

1. **Turnstile** — easiest, no tunnel needed (just add `localhost` domain)
2. **SES** — needs AWS account but no tunnel; test with magic link login
3. **Paystack** — needs ngrok tunnel for webhooks; most complex flow
4. **R2 Storage** — needs Cloudflare account; verify on startup
5. **Litestream** — Docker only; test last after everything else works

### Environment Variable Approach

Rather than modifying `appsettings.Development.json`, use environment variables to switch one provider at a time:

```powershell
# Test Turnstile only (everything else stays mock)
$env:Turnstile__Provider = "Cloudflare"
$env:Turnstile__SiteKey = "..."
$env:Turnstile__SecretKey = "..."
dotnet run
```

This way you don't risk committing secrets to config files.

### Check Provider Registration on Startup

The app logs which providers are active on startup. Look for:
- `[MOCK BILLING]` — mock billing active
- `[Paystack]` — real Paystack active  
- Email provider logs on first send attempt

### Clean State Between Tests

```powershell
# Delete all local databases to start fresh
Remove-Item src/db/core.db, src/db/audit.db -ErrorAction SilentlyContinue
Remove-Item src/db/tenants/* -ErrorAction SilentlyContinue
```

The dev seeder will recreate everything on next startup (when `DevSeed:Enabled=true`).

### Security Reminders

- **Never commit** real API keys to source control
- Use `.env` files (already in `.gitignore`) or environment variables
- Paystack test keys (`sk_test_`) are safe for development but keep them private
- Rotate any key that's been accidentally committed
