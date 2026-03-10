using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using saas.Data.Core;
using saas.Infrastructure.Services;
using saas.Modules.Billing;
using saas.Modules.Billing.Services;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class BillingModuleTests
{
    [Fact]
    public void RegisterServices_MockProvider_RegistersMockBillingService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Add CoreDbContext (required by MockBillingService)
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<CoreDbContext>(o => o.UseSqlite(connection));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Provider"] = "Mock"
            })
            .Build();

        var module = new BillingModule();
        module.RegisterServices(services, config);

        var provider = services.BuildServiceProvider();
        var billing = provider.GetService<IBillingService>();

        Assert.NotNull(billing);
        Assert.IsType<MockBillingService>(billing);

        connection.Close();
    }

    [Fact]
    public void RegisterServices_PaystackProvider_RegistersPaystackBillingService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Add CoreDbContext
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<CoreDbContext>(o => o.UseSqlite(connection));

        // Add IAuditWriter stub
        services.AddSingleton<IAuditWriter>(new StubAuditWriter());

        // Add IEmailService stub
        services.AddSingleton<IEmailService>(new StubEmailService());

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Provider"] = "Paystack",
                ["Billing:Paystack:SecretKey"] = "sk_test_xxx",
                ["Billing:Paystack:PublicKey"] = "pk_test_xxx",
                ["Billing:Paystack:WebhookSecret"] = "whsec_test",
                ["Billing:Paystack:CallbackBaseUrl"] = "https://localhost"
            })
            .Build();

        var module = new BillingModule();
        module.RegisterServices(services, config);

        var provider = services.BuildServiceProvider();
        var billing = provider.GetService<IBillingService>();

        Assert.NotNull(billing);
        Assert.IsType<PaystackBillingService>(billing);

        connection.Close();
    }

    [Fact]
    public void RegisterServices_DefaultProvider_RegistersMockBillingService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<CoreDbContext>(o => o.UseSqlite(connection));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var module = new BillingModule();
        module.RegisterServices(services, config);

        var provider = services.BuildServiceProvider();
        var billing = provider.GetService<IBillingService>();

        Assert.NotNull(billing);
        Assert.IsType<MockBillingService>(billing);

        connection.Close();
    }

    [Fact]
    public void RegisterServices_AlwaysRegistersInvoiceGenerator()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<CoreDbContext>(o => o.UseSqlite(connection));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Provider"] = "Mock"
            })
            .Build();

        var module = new BillingModule();
        module.RegisterServices(services, config);

        var provider = services.BuildServiceProvider();
        var invoiceGen = provider.GetService<InvoiceGenerator>();

        Assert.NotNull(invoiceGen);

        connection.Close();
    }

    [Fact]
    public void Module_HasCorrectName()
    {
        var module = new BillingModule();
        Assert.Equal("Billing", module.Name);
    }

    [Fact]
    public void Module_HasWebhookControllerViewPath()
    {
        var module = new BillingModule();
        Assert.True(module.ControllerViewPaths.ContainsKey("PaystackWebhook"));
    }

    private class StubAuditWriter : IAuditWriter
    {
        public ValueTask WriteAsync(AuditEntry entry) => ValueTask.CompletedTask;
    }

    private class StubEmailService : IEmailService
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message) => Task.FromResult(EmailSendResult.Succeeded());
        public Task<EmailSendResult> SendMagicLinkAsync(string to, string magicLinkUrl) => Task.FromResult(EmailSendResult.Succeeded());
    }
}
