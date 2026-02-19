# Billing Module

Paystack payment integration for subscription billing.

## Architecture Note

The Billing module contains only **services and data logic** — it has no controllers or views of its own.

Billing **UI** lives in the **TenantAdmin** module:

| Controller | Location |
|---|---|
| `TenantBillingController` | `Modules/TenantAdmin/Controllers/TenantBillingController.cs` |
| Billing views | `Modules/TenantAdmin/Views/TenantBilling/` |

This design keeps all tenant-facing admin pages under a single layout and authorization policy while the Billing module stays focused on payment orchestration.

## Structure

```
Billing/
├── BillingModule.cs           — IModule implementation
├── Entities/
│   ├── Plan.cs                — Plan entity (core DB)
│   ├── Subscription.cs        — Subscription entity (core DB)
│   ├── Invoice.cs             — Invoice entity (core DB)
│   └── Payment.cs             — Payment entity (core DB)
├── Data/
│   └── BillingConfigurations.cs — EF configs (ICoreEntityConfiguration)
├── Services/
│   ├── PaystackBillingService.cs — Paystack API integration
│   ├── PaystackClient.cs         — Low-level HTTP client
│   ├── InvoiceGeneratorService.cs — Invoice creation
│   └── WebhookSignatureValidator.cs — Webhook verification
└── Views/Billing/              — Plan selection, checkout, billing management
```

## Configuration

```json
{
  "Billing": {
    "Provider": "Mock",           // or "Paystack"
    "Paystack": {
      "SecretKey": "",
      "PublicKey": "",
      "WebhookSecret": "",
      "CallbackBaseUrl": "https://myapp.com"
    }
  }
}
```

- **Mock** — no-op billing for development, all operations succeed immediately
- **Paystack** — real payment processing with webhook callbacks

## Webhook Flow

1. Paystack sends POST to `/billing/webhook`
2. `WebhookSignatureValidator` verifies HMAC signature
3. Events processed: `charge.success`, `subscription.create`, `subscription.disable`, `invoice.payment_failed`
