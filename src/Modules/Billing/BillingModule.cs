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

        var provider = configuration.GetValue<string>("Billing:Provider") ?? "Mock";

        if (provider.Equals("Paystack", StringComparison.OrdinalIgnoreCase))
        {
            // Paystack configuration
            services.Configure<PaystackOptions>(configuration.GetSection(PaystackOptions.SectionName));

            // Typed HTTP client for Paystack API
            services.AddHttpClient<PaystackClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.paystack.co/");
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.AddScoped<IBillingService, PaystackBillingService>();

            // Background plan sync — only in Paystack mode
            services.AddHostedService<PaystackPlanSyncService>();

            // Background subscription reconciliation — every 6 hours
            services.AddHostedService<PaystackSubscriptionSyncService>();
        }
        else
        {
            services.AddScoped<IBillingService, MockBillingService>();
        }

        // Invoice generator is used by both providers
        services.AddScoped<InvoiceGenerator>();

        // Usage metering
        services.AddScoped<IUsageMeteringService, UsageMeteringService>();
    }
}
