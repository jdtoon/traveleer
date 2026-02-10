using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Fixtures;

/// <summary>
/// Shared test fixture that boots the app with a seeded demo tenant.
/// Uses Development environment so DevSeed provisions `demo` tenant automatically.
/// </summary>
public class AppFixture : IDisposable
{
    public WebApplicationFactory<Program> Factory { get; }

    public AppFixture()
    {
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("DevSeed:Enabled", "true");
                builder.UseSetting("DevSeed:TenantSlug", "demo");
                builder.UseSetting("DevSeed:AdminEmail", "admin@demo.local");
                builder.UseSetting("DevSeed:MemberEmail", "member@demo.local");
                builder.UseSetting("DevSeed:PlanSlug", "starter");
                builder.UseSetting("Billing:Provider", "Mock");
                builder.UseSetting("Turnstile:Provider", "Mock");
                builder.UseSetting("Email:Provider", "Console");
                builder.UseSetting("FeatureFlags:AllEnabledLocally", "true");
                builder.UseSetting("Storage:Provider", "Local");
            });

        // Force the server to start (and run all initialization including seeding)
        _ = Factory.Server;
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Create an HtmxTestClient for testing public (non-tenant) routes.</summary>
    public HtmxTestClient<Program> CreateClient() => new(Factory);

    /// <summary>Create an HtmxTestClient for testing tenant routes under /{slug}/.</summary>
    public HtmxTestClient<Program> CreateTenantClient(string slug = "demo") => new(Factory);
}
