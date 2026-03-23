using System.Net.Http.Headers;
using saas.Infrastructure.Services;
using saas.Modules.Billing.Models;
using saas.Modules.Billing.Services;
using saas.Shared;

namespace saas.Modules.Billing;

public class BillingModule : IModule
{
    public string Name => "Billing";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["PaystackWebhook"] = "Billing"
    };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Billing options (tax, company info, trials, grace period, feature toggles)
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));
        services.Configure<PaystackOptions>(configuration.GetSection(PaystackOptions.SectionName));

        // Register the typed client in all modes because downstream billing services
        // depend on its constructor type even when tests run with the mock provider.
        services.AddHttpClient<PaystackClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.paystack.co/");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        var provider = configuration.GetValue<string>("Billing:Provider") ?? "Mock";

        if (provider.Equals("Paystack", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IBillingService, PaystackBillingService>();

            // Variable charge orchestrator requires PaystackClient — only available in Paystack mode
            services.AddScoped<IVariableChargeService, VariableChargeService>();

            // Background plan sync — only in Paystack mode
            services.AddHostedService<PaystackPlanSyncService>();

            // Background subscription reconciliation — every 6 hours
            services.AddHostedService<PaystackSubscriptionSyncService>();
        }
        else
        {
            services.AddScoped<IBillingService, MockBillingService>();

            // No-op variable charge service for non-Paystack providers
            services.AddScoped<IVariableChargeService, NullVariableChargeService>();
        }

        // Invoice generator (legacy) is used by both providers
        services.AddScoped<InvoiceGenerator>();

        // Core billing services
        services.AddScoped<ICreditService, CreditService>();
        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<IInvoiceEngine, InvoiceEngine>();
        services.AddScoped<ISeatBillingService, SeatBillingService>();
        services.AddScoped<IDunningService, DunningService>();
        services.AddScoped<IAddOnService, AddOnService>();

        // Lazy<IVariableChargeService> for DunningService (breaks circular dependency)
        services.AddScoped(sp => new Lazy<IVariableChargeService>(() => sp.GetRequiredService<IVariableChargeService>()));

        // Usage metering + billing
        services.AddScoped<IUsageMeteringService, UsageBillingService>();
        services.AddScoped<IUsageBillingService, UsageBillingService>();
    }
}
