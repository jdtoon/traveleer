using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Data.Audit;
using saas.Data.Core;
using saas.Modules.Billing.DTOs;
using saas.Modules.Billing.Models;
using saas.Modules.Billing.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class PaystackBillingServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly InvoiceGenerator _invoiceGenerator;
    private readonly PaystackOptions _options;
    private readonly StubAuditWriter _auditWriter;
    private readonly StubEmailService _emailService;

    // Test data
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _freePlanId = Guid.NewGuid();
    private readonly Guid _proPlanId = Guid.NewGuid();

    public PaystackBillingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new CoreDbContext(dbOptions);
        _db.Database.EnsureCreated();

        _invoiceGenerator = new InvoiceGenerator(_db);
        _options = new PaystackOptions
        {
            SecretKey = "sk_test_xxx",
            PublicKey = "pk_test_xxx",
            WebhookSecret = "whsec_test_secret",
            CallbackBaseUrl = "https://localhost:5001"
        };
        _auditWriter = new StubAuditWriter();
        _emailService = new StubEmailService();

        SeedTestData();
    }

    private void SeedTestData()
    {
        _db.Plans.Add(new Plan
        {
            Id = _freePlanId,
            Name = "Free",
            Slug = "free",
            MonthlyPrice = 0,
            Currency = "ZAR",
            SortOrder = 0
        });

        _db.Plans.Add(new Plan
        {
            Id = _proPlanId,
            Name = "Pro",
            Slug = "pro",
            MonthlyPrice = 499,
            AnnualPrice = 4990,
            Currency = "ZAR",
            SortOrder = 1,
            PaystackMonthlyPlanCode = "PLN_test123"
        });

        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Slug = "test-tenant",
            ContactEmail = "admin@test.com",
            Status = TenantStatus.Active,
            PlanId = _freePlanId
        });

        _db.SaveChanges();
    }

    private PaystackBillingService CreateService(PaystackClient? client = null)
    {
        // For tests that don't hit Paystack, we pass a stub client
        client ??= CreateStubPaystackClient();

        return new PaystackBillingService(
            client,
            _db,
            _invoiceGenerator,
            Options.Create(_options),
            _auditWriter,
            _emailService,
            NullLogger<PaystackBillingService>.Instance);
    }

    private static PaystackClient CreateStubPaystackClient()
    {
        // Create a PaystackClient with a no-op HttpClient (won't be called in these tests)
        var httpClient = new HttpClient(new StubHttpHandler());
        return new PaystackClient(
            httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);
    }

    [Fact]
    public async Task InitializeSubscription_FreePlan_CreatesActiveSubscription()
    {
        var service = CreateService();
        var result = await service.InitializeSubscriptionAsync(new SubscriptionInitRequest(
            _tenantId, "admin@test.com", _freePlanId, BillingCycle.Monthly));

        Assert.True(result.Success);
        Assert.Null(result.PaymentUrl); // No payment needed

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == _tenantId);
        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task InitializeSubscription_InvalidPlan_ReturnsError()
    {
        var service = CreateService();
        var result = await service.InitializeSubscriptionAsync(new SubscriptionInitRequest(
            _tenantId, "admin@test.com", Guid.NewGuid(), BillingCycle.Monthly));

        Assert.False(result.Success);
        Assert.Equal("Plan not found", result.Error);
    }

    [Fact]
    public async Task GetSubscriptionStatus_NoSubscription_ReturnsNull()
    {
        var service = CreateService();
        var status = await service.GetSubscriptionStatusAsync(Guid.NewGuid());

        Assert.Null(status);
    }

    [Fact]
    public async Task GetSubscriptionStatus_WithSubscription_ReturnsStatus()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _freePlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = CreateService();
        var status = await service.GetSubscriptionStatusAsync(_tenantId);

        Assert.Equal(SubscriptionStatus.Active, status);
    }

    [Fact]
    public async Task CancelSubscription_ActiveSubscription_SetsCancelled()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _freePlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.CancelSubscriptionAsync(_tenantId);

        Assert.True(result);

        var subscription = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.NotNull(subscription.CancelledAt);
    }

    [Fact]
    public async Task CancelSubscription_WithPaystackCode_FetchesEmailTokenAndDisables()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = "SUB_test_cancel"
        });
        await _db.SaveChangesAsync();

        // Use a handler that returns proper subscription detail with email_token
        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["subscription/SUB_test_cancel"] = """{"status":true,"data":{"subscription_code":"SUB_test_cancel","email_token":"tok_test123","status":"active"}}""",
            ["subscription/disable"] = """{"status":true,"message":"Subscription disabled"}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        var result = await service.CancelSubscriptionAsync(_tenantId);

        Assert.True(result);
        var subscription = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
    }

    [Fact]
    public async Task ProcessWebhook_SubscriptionCreate_UpdatesCodeFromTopLevel()
    {
        // Simulate the state after InitializeSubscriptionAsync: 
        // subscription stored with transaction reference, not real SUB_ code
        var transactionRef = "txn_ref_abc";
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = transactionRef
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        // Paystack subscription.create puts subscription_code at data level, not nested
        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "subscription.create",
            Data = new PaystackWebhookData
            {
                Reference = transactionRef,
                SubscriptionCode = "SUB_real_code",
                Customer = new PaystackWebhookCustomer
                {
                    Code = "CUS_test_123",
                    Email = "admin@test.com"
                }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var updated = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal("SUB_real_code", updated.PaystackSubscriptionCode);
        Assert.Equal("CUS_test_123", updated.PaystackCustomerCode);
    }

    [Fact]
    public async Task ProcessWebhook_SubscriptionCreate_FindsByCustomerEmail()
    {
        // Subscription with non-SUB_ code (transaction reference) and no matching reference in webhook
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = "some_old_ref"
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        // Webhook has no matching reference, but customer email matches tenant
        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "subscription.create",
            Data = new PaystackWebhookData
            {
                Reference = "different_ref",
                SubscriptionCode = "SUB_found_by_email",
                Customer = new PaystackWebhookCustomer
                {
                    Code = "CUS_email_match",
                    Email = "admin@test.com" // matches _tenant.ContactEmail
                }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var updated = await _db.Subscriptions.FirstAsync(s => s.TenantId == _tenantId);
        Assert.Equal("SUB_found_by_email", updated.PaystackSubscriptionCode);
    }

    [Fact]
    public async Task ProcessWebhook_InvoiceCreate_LooksUpBySubscriptionCode()
    {
        var subscriptionCode = "SUB_invoice_test";
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = subscriptionCode
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        // Invoice event with subscription code but no metadata
        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "invoice.create",
            Data = new PaystackWebhookData
            {
                Reference = "inv_ref_123",
                Amount = 49900,
                Currency = "ZAR",
                Subscription = new PaystackWebhookSubscription { Code = subscriptionCode }
                // No metadata — testing the subscription code lookup path
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        // Verify invoice was generated via subscription code lookup
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.PaystackReference == "inv_ref_123");
        Assert.NotNull(invoice);
        Assert.Equal(499m, invoice.Total);
    }

    [Fact]
    public async Task ProcessWebhook_InvoiceUpdate_UpdatesInvoiceStatus()
    {
        // Create a subscription and invoice first
        var subscription = new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = "SUB_inv_update"
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var invoice = await _invoiceGenerator.GenerateAsync(_tenantId, subscription.Id, 499m, "ZAR");
        invoice.PaystackReference = "inv_update_ref";
        await _db.SaveChangesAsync();

        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "invoice.update",
            Data = new PaystackWebhookData
            {
                Reference = "inv_update_ref",
                Status = "success",
                Amount = 49900
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var updatedInvoice = await _db.Invoices.FirstAsync(i => i.PaystackReference == "inv_update_ref");
        Assert.Equal(InvoiceStatus.Paid, updatedInvoice.Status);
    }

    [Fact]
    public async Task CancelSubscription_NoSubscription_ReturnsFalse()
    {
        var service = CreateService();
        var result = await service.CancelSubscriptionAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task ProcessWebhook_InvalidSignature_ReturnsError()
    {
        var service = CreateService();
        var result = await service.ProcessWebhookAsync("{}", "invalid-signature");

        Assert.False(result.Success);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public async Task ProcessWebhook_ChargeSuccess_CreatesPayment()
    {
        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "charge.success",
            Data = new PaystackWebhookData
            {
                Reference = "txn_test_123",
                Amount = 49900,
                Currency = "ZAR",
                GatewayResponse = "Approved",
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = _tenantId.ToString()
                }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.PaystackReference == "txn_test_123");
        Assert.NotNull(payment);
        Assert.Equal(499m, payment.Amount);
        Assert.Equal(PaymentStatus.Success, payment.Status);
    }

    [Fact]
    public async Task ProcessWebhook_ChargeSuccess_Idempotent()
    {
        // Add existing payment
        _db.Payments.Add(new Payment
        {
            TenantId = _tenantId,
            Amount = 499m,
            Currency = "ZAR",
            Status = PaymentStatus.Success,
            PaystackReference = "txn_duplicate",
            TransactionDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "charge.success",
            Data = new PaystackWebhookData
            {
                Reference = "txn_duplicate",
                Amount = 49900,
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = _tenantId.ToString()
                }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        // Should still only be 1 payment
        var count = await _db.Payments.CountAsync(p => p.PaystackReference == "txn_duplicate");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ProcessWebhook_PaymentFailed_SetsStatusToPastDue()
    {
        var subscriptionCode = "SUB_test_fail";
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = subscriptionCode
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "invoice.payment_failed",
            Data = new PaystackWebhookData
            {
                Subscription = new PaystackWebhookSubscription { Code = subscriptionCode }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var subscription = await _db.Subscriptions
            .FirstAsync(s => s.PaystackSubscriptionCode == subscriptionCode);
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
    }

    [Fact]
    public async Task ProcessWebhook_SubscriptionDisabled_SetsCancelled()
    {
        var subscriptionCode = "SUB_test_disabled";
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = subscriptionCode
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "subscription.disable",
            Data = new PaystackWebhookData
            {
                Subscription = new PaystackWebhookSubscription { Code = subscriptionCode }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var subscription = await _db.Subscriptions
            .FirstAsync(s => s.PaystackSubscriptionCode == subscriptionCode);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.NotNull(subscription.CancelledAt);
        Assert.NotNull(subscription.EndDate);
    }

    [Fact]
    public async Task ProcessWebhook_SubscriptionNotRenew_SetsNonRenewing()
    {
        var subscriptionCode = "SUB_test_not_renew";
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = subscriptionCode
        });
        await _db.SaveChangesAsync();

        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "subscription.not_renew",
            Data = new PaystackWebhookData
            {
                Subscription = new PaystackWebhookSubscription { Code = subscriptionCode }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);

        var subscription = await _db.Subscriptions
            .FirstAsync(s => s.PaystackSubscriptionCode == subscriptionCode);
        Assert.Equal(SubscriptionStatus.NonRenewing, subscription.Status);
        Assert.Null(subscription.EndDate); // EndDate not set yet — set when disabled
    }

    [Fact]
    public async Task ProcessWebhook_ExpiringCards_SendsEmailToTenantAdmin()
    {
        var subscriptionCode = "SUB_test_expiring";
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = subscriptionCode
        });
        await _db.SaveChangesAsync();

        // Use handler that returns subscription with manage_link
        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            [$"subscription/{subscriptionCode}"] = """{"status":true,"data":{"subscription_code":"SUB_test_expiring","email_token":"tok","status":"active","manage_link":"https://paystack.com/manage/123"}}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "subscription.expiring_cards",
            Data = new PaystackWebhookData
            {
                Subscription = new PaystackWebhookSubscription { Code = subscriptionCode }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);
        Assert.Single(_emailService.SentMessages);
        Assert.Equal("admin@test.com", _emailService.SentMessages[0].To);
        Assert.Contains("expiring", _emailService.SentMessages[0].Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Update Card Now", _emailService.SentMessages[0].HtmlBody);
    }

    [Fact]
    public async Task ChangePlan_FromNonRenewing_CancelsOldAndUpdates()
    {
        // Set up a non-renewing paid subscription
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.NonRenewing,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            PaystackSubscriptionCode = "SUB_old_nonrenewing"
        });

        var tenant = await _db.Tenants.FindAsync(_tenantId);
        tenant!.PlanId = _proPlanId;
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["subscription/SUB_old_nonrenewing"] = """{"status":true,"data":{"subscription_code":"SUB_old_nonrenewing","email_token":"tok_nr","status":"non-renewing"}}""",
            ["subscription/disable"] = """{"status":true,"message":"Subscription disabled"}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        var result = await service.ChangePlanAsync(_tenantId, _freePlanId);

        Assert.True(result.Success);

        var sub = await _db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == _tenantId);
        Assert.NotNull(sub);
        Assert.Equal(_freePlanId, sub.PlanId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
    }

    [Fact]
    public async Task ChangePlan_SuspendedTenant_ReactivatesTenant()
    {
        // Suspend the tenant
        var tenant = await _db.Tenants.FindAsync(_tenantId);
        tenant!.Status = TenantStatus.Suspended;
        tenant.PlanId = _proPlanId;

        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.PastDue,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1)
        });
        await _db.SaveChangesAsync();

        var service = CreateService();
        var result = await service.ChangePlanAsync(_tenantId, _freePlanId);

        Assert.True(result.Success);

        var updatedTenant = await _db.Tenants.FindAsync(_tenantId);
        Assert.Equal(TenantStatus.Active, updatedTenant!.Status);
    }

    [Fact]
    public async Task ReconcileSubscriptions_UpdatesDriftedStatus()
    {
        // Create an active subscription that Paystack says is non-renewing
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = "SUB_reconcile_test"
        });
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["subscription/SUB_reconcile_test"] = """{"status":true,"data":{"subscription_code":"SUB_reconcile_test","email_token":"tok","status":"non-renewing"}}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        await service.ReconcileSubscriptionsAsync();

        var sub = await _db.Subscriptions
            .FirstAsync(s => s.PaystackSubscriptionCode == "SUB_reconcile_test");
        Assert.Equal(SubscriptionStatus.NonRenewing, sub.Status);
    }

    [Fact]
    public async Task ReconcileSubscriptions_CancelledOnPaystack_SetsCancelledLocally()
    {
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow,
            PaystackSubscriptionCode = "SUB_reconcile_cancelled"
        });
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["subscription/SUB_reconcile_cancelled"] = """{"status":true,"data":{"subscription_code":"SUB_reconcile_cancelled","email_token":"tok","status":"cancelled"}}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        await service.ReconcileSubscriptionsAsync();

        var sub = await _db.Subscriptions
            .FirstAsync(s => s.PaystackSubscriptionCode == "SUB_reconcile_cancelled");
        Assert.Equal(SubscriptionStatus.Cancelled, sub.Status);
        Assert.NotNull(sub.EndDate);
        Assert.NotNull(sub.CancelledAt);
    }

    [Fact]
    public async Task ProcessWebhook_UnhandledEvent_ReturnsSuccess()
    {
        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "some.unknown.event",
            Data = new PaystackWebhookData()
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        var result = await service.ProcessWebhookAsync(payload, signature);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessWebhook_ChargeSuccess_ReactivatesSuspendedTenant()
    {
        // Set tenant to suspended
        var tenant = await _db.Tenants.FindAsync(_tenantId);
        tenant!.Status = TenantStatus.Suspended;
        await _db.SaveChangesAsync();

        var service = CreateService();

        var webhookPayload = new PaystackWebhookEvent
        {
            Event = "charge.success",
            Data = new PaystackWebhookData
            {
                Reference = "txn_reactivate",
                Amount = 49900,
                Currency = "ZAR",
                Metadata = new Dictionary<string, object>
                {
                    ["tenant_id"] = _tenantId.ToString()
                }
            }
        };

        var payload = JsonSerializer.Serialize(webhookPayload);
        var signature = ComputeSignature(payload, _options.WebhookSecret);

        await service.ProcessWebhookAsync(payload, signature);

        var updatedTenant = await _db.Tenants.FindAsync(_tenantId);
        Assert.Equal(TenantStatus.Active, updatedTenant!.Status);
    }

    // ── ChangePlan Tests ────────────────────────────────────────────

    [Fact]
    public async Task ChangePlan_ToFreePlan_CancelsPaystackAndUpdatesSubscription()
    {
        // Set up an active paid subscription
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _proPlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1),
            PaystackSubscriptionCode = "SUB_old_paid"
        });

        // Update tenant to pro plan
        var tenant = await _db.Tenants.FindAsync(_tenantId);
        tenant!.PlanId = _proPlanId;
        await _db.SaveChangesAsync();

        // Set up routing handler for subscription fetch + disable
        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["subscription/SUB_old_paid"] = """{"status":true,"data":{"subscription_code":"SUB_old_paid","email_token":"tok_change","status":"active"}}""",
            ["subscription/disable"] = """{"status":true,"message":"Subscription disabled"}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        var result = await service.ChangePlanAsync(_tenantId, _freePlanId);

        Assert.True(result.Success);
        Assert.Null(result.PaymentUrl); // Free plan — no Paystack redirect

        // Subscription should be updated in-place to the free plan
        var sub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == _tenantId);
        Assert.NotNull(sub);
        Assert.Equal(_freePlanId, sub.PlanId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Null(sub.PaystackSubscriptionCode); // Paystack code cleared

        // Tenant's PlanId should be updated
        var updatedTenant = await _db.Tenants.FindAsync(_tenantId);
        Assert.Equal(_freePlanId, updatedTenant!.PlanId);
    }

    [Fact]
    public async Task ChangePlan_ToPaidPlan_UpdatesSubAndReturnsPaymentUrl()
    {
        // Set up an active free subscription
        _db.Subscriptions.Add(new Subscription
        {
            TenantId = _tenantId,
            PlanId = _freePlanId,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            StartDate = DateTime.UtcNow.AddMonths(-1)
        });
        await _db.SaveChangesAsync();

        // Set up handler for transaction initialization
        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["transaction/initialize"] = """{"status":true,"data":{"authorization_url":"https://paystack.com/pay/test","reference":"ref_change_123","access_code":"ac_test"}}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        var result = await service.ChangePlanAsync(_tenantId, _proPlanId);

        Assert.True(result.Success);
        Assert.Equal("https://paystack.com/pay/test", result.PaymentUrl);

        // Subscription should be updated in-place with the new plan + Paystack reference
        var sub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == _tenantId);
        Assert.NotNull(sub);
        Assert.Equal(_proPlanId, sub.PlanId);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal("ref_change_123", sub.PaystackSubscriptionCode);

        // Tenant's PlanId should be updated
        var updatedTenant = await _db.Tenants.FindAsync(_tenantId);
        Assert.Equal(_proPlanId, updatedTenant!.PlanId);
    }

    [Fact]
    public async Task ChangePlan_InvalidPlan_ReturnsError()
    {
        var service = CreateService();
        var result = await service.ChangePlanAsync(_tenantId, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("Plan not found", result.Error);
    }

    // ── UpdatePlanInGateway Tests ───────────────────────────────────

    [Fact]
    public async Task UpdatePlanInGateway_NoPlanCode_CreatesPlanOnPaystack()
    {
        // Add a paid plan with no Paystack code
        var newPlanId = Guid.NewGuid();
        _db.Plans.Add(new Plan
        {
            Id = newPlanId,
            Name = "Starter",
            Slug = "starter",
            MonthlyPrice = 199,
            Currency = "ZAR",
            SortOrder = 2,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpHandler(new Dictionary<string, string>
        {
            ["plan"] = """{"status":true,"data":{"plan_code":"PLN_new_created","name":"Starter (Monthly)"}}"""
        });
        var httpClient = new HttpClient(handler);
        var client = new PaystackClient(httpClient,
            Options.Create(new PaystackOptions { SecretKey = "sk_test" }),
            NullLogger<PaystackClient>.Instance);

        var service = CreateService(client);
        var result = await service.UpdatePlanInGatewayAsync(newPlanId);

        Assert.True(result);

        // Plan should now have the Paystack code
        var updatedPlan = await _db.Plans.FindAsync(newPlanId);
        Assert.Equal("PLN_new_created", updatedPlan!.PaystackMonthlyPlanCode);
    }

    [Fact]
    public async Task UpdatePlanInGateway_FreePlan_ReturnsFalse()
    {
        var service = CreateService();
        var result = await service.UpdatePlanInGatewayAsync(_freePlanId);

        Assert.False(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static string ComputeSignature(string payload, string secret)
    {
        var hash = System.Security.Cryptography.HMACSHA512.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Stubs ───────────────────────────────────────────────────────

    private class StubAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public ValueTask WriteAsync(AuditEntry entry)
        {
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }

    private class StubEmailService : IEmailService
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task SendAsync(EmailMessage message)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task SendMagicLinkAsync(string to, string magicLinkUrl) => Task.CompletedTask;
    }

    private class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Return a generic success response
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":true,"data":{}}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// HTTP handler that routes requests to specific JSON responses based on URL path matching.
    /// </summary>
    private class RoutingHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes;

        public RoutingHttpHandler(Dictionary<string, string> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery.TrimStart('/') ?? "";
            foreach (var route in _routes)
            {
                if (path.Contains(route.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(route.Value,
                            System.Text.Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":true,"data":{}}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
