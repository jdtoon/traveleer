using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Onboarding.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Onboarding;

public class OnboardingIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public OnboardingIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task OnboardingPage_RendersFullLayout()
    {
        await ResetStateAsync();

        var response = await _client.GetAsync($"/{TenantSlug}/onboarding");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Launch demo with confidence.");
    }

    [Fact]
    public async Task OnboardingStepPartial_RendersWithoutLayout()
    {
        await ResetStateAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/onboarding/step/1");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Shape the public identity clients will see.");
    }

    [Fact]
    public async Task OnboardingFlow_UserCanCompleteSetup_AndDashboardStopsRedirecting()
    {
        await ResetStateAsync();

        var dashboard = await _client.GetAsync($"/{TenantSlug}");
        dashboard.AssertStatus(System.Net.HttpStatusCode.Redirect);

        var page = await _client.GetAsync($"/{TenantSlug}/onboarding");
        page.AssertSuccess();
        await page.AssertElementExistsAsync($"form[action='/{TenantSlug}/onboarding/step/identity']");

        var identityResponse = await _client.SubmitFormAsync(page, $"form[action='/{TenantSlug}/onboarding/step/identity']", new Dictionary<string, string>
        {
            ["AgencyName"] = "Acacia Journeys",
            ["PublicContactEmail"] = "quotes@acacia.test",
            ["ContactPhone"] = "+27 11 555 1111",
            ["Website"] = "https://acacia.test",
            ["PrimaryColor"] = "#0F766E",
            ["SecondaryColor"] = "#1E293B"
        });

        identityResponse.AssertSuccess();
        identityResponse.AssertToast("Identity saved.");
        await identityResponse.AssertContainsAsync("Choose the defaults your team should start from.");

        var defaultsResponse = await _client.SubmitFormAsync(identityResponse, $"form[action='/{TenantSlug}/onboarding/step/defaults']", new Dictionary<string, string>
        {
            ["QuotePrefix"] = "ACJ",
            ["DefaultQuoteValidityDays"] = "21",
            ["DefaultQuoteMarkupPercentage"] = "18",
            ["PreferredWorkspace"] = "quotes",
            ["QuoteResetSequenceYearly"] = "true"
        });

        defaultsResponse.AssertSuccess();
        defaultsResponse.AssertToast("Defaults saved.");
        await defaultsResponse.AssertContainsAsync("Review your launch settings.");

        var completeResponse = await _client.SubmitFormAsync(defaultsResponse, $"form[action='/{TenantSlug}/onboarding/complete']", new Dictionary<string, string>());
        completeResponse.AssertStatus(System.Net.HttpStatusCode.Redirect);

        await using var db = OpenTenantDb();
        var state = await db.TenantOnboardingStates.SingleAsync();
        Assert.NotNull(state.CompletedAt);

        var dashboardAfterComplete = await _client.GetAsync($"/{TenantSlug}");
        dashboardAfterComplete.AssertSuccess();
        await dashboardAfterComplete.AssertContainsAsync("Dashboard");
    }

    [Fact]
    public async Task IdentityStep_OnInvalidSubmit_ReturnsErrors()
    {
        await ResetStateAsync();

        var page = await _client.GetAsync($"/{TenantSlug}/onboarding");
        page.AssertSuccess();

        var response = await _client.SubmitFormAsync(page, $"form[action='/{TenantSlug}/onboarding/step/identity']", new Dictionary<string, string>
        {
            ["AgencyName"] = "Acacia Journeys",
            ["PublicContactEmail"] = "quotes@acacia.test",
            ["Website"] = "not-a-url",
            ["PrimaryColor"] = "#0F766E",
            ["SecondaryColor"] = "#1E293B"
        });

        response.AssertSuccess();
        response.AssertToast("Please fix the errors below.");
        await response.AssertContainsAsync("Enter a valid website URL.");
        await response.AssertContainsAsync("Shape the public identity clients will see.");
    }

    [Fact]
    public async Task OnboardingPage_WhenCompleted_DashboardLoadsWithoutRedirect()
    {
        await ResetStateAsync(completed: true);

        var response = await _client.GetAsync($"/{TenantSlug}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Dashboard");
    }

    [Fact]
    public async Task OnboardingPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/onboarding");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private async Task ResetStateAsync(bool completed = false)
    {
        await using var db = OpenTenantDb();
        var existing = await db.TenantOnboardingStates.ToListAsync();
        if (existing.Count > 0)
        {
            db.TenantOnboardingStates.RemoveRange(existing);
        }

        if (completed)
        {
            db.TenantOnboardingStates.Add(new TenantOnboardingState
            {
                CurrentStep = 3,
                IdentityCompletedAt = DateTime.UtcNow,
                DefaultsCompletedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
