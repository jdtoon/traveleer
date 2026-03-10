using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Branding.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Branding;

public class BrandingIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public BrandingIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task BrandingPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/branding");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Branding");
    }

    [Fact]
    public async Task BrandingThemeVarsPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/branding/theme-vars");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("--color-primary");
    }

    [Fact]
    public async Task BrandingPage_UserCanSaveSettingsAndNewQuotesUseThem()
    {
        var page = await _client.GetAsync($"/{TenantSlug}/branding");
        page.AssertSuccess();
        await page.AssertElementExistsAsync($"form[hx-post='/{TenantSlug}/branding/save']");

        var response = await _client.SubmitFormAsync(page, $"form[hx-post='/{TenantSlug}/branding/save']", new Dictionary<string, string>
        {
            ["AgencyName"] = "Acacia Journeys",
            ["PublicContactEmail"] = "quotes@acacia.test",
            ["ContactPhone"] = "+27 11 555 1111",
            ["Website"] = "https://acacia.test",
            ["PrimaryColor"] = "#0F766E",
            ["SecondaryColor"] = "#1E293B",
            ["QuotePrefix"] = "ACJ",
            ["QuoteNumberFormat"] = "{PREFIX}-{YEAR}-{SEQ:4}",
            ["NextQuoteSequence"] = "7",
            ["QuoteResetSequenceYearly"] = "true",
            ["DefaultQuoteValidityDays"] = "21",
            ["DefaultQuoteMarkupPercentage"] = "18",
            ["PdfFooterText"] = "Subject to supplier reconfirmation."
        });

        response.AssertSuccess();
        response.AssertToast("Branding updated.");
        response.AssertTrigger("branding.refresh");
        await response.AssertContainsAsync("Acacia Journeys");
        await response.AssertContainsAsync("ACJ-");

        await using var db = OpenTenantDb();
        var settings = await db.BrandingSettings.SingleAsync();
        Assert.Equal("Acacia Journeys", settings.AgencyName);
        Assert.Equal("ACJ", settings.QuotePrefix);
        Assert.Equal(21, settings.DefaultQuoteValidityDays);
        Assert.Equal(18m, settings.DefaultQuoteMarkupPercentage);

        var quotePage = await _client.GetAsync($"/{TenantSlug}/quotes/new");
        quotePage.AssertSuccess();
        await quotePage.AssertContainsAsync("ACJ-");
        await quotePage.AssertContainsAsync("value=\"18\"");
    }

    [Fact]
    public async Task BrandingPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/branding");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
