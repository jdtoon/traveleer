using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Reports;

public class ReportIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public ReportIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page Load ──

    [Fact]
    public async Task ReportsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertContainsAsync("Reports");
    }

    [Fact]
    public async Task ReportsPage_WithDateRange_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports?range=year");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertContainsAsync("Reports");
    }

    // ── Layer 2: Partial Isolation ──

    [Fact]
    public async Task RevenueMonthlyWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/revenue-monthly?range=year");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task RevenueYtdWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/revenue-ytd");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task BookingsStatusWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/bookings-status?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task BookingsRecentWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/bookings-recent");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task QuotesConversionWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/quotes-conversion?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task QuotesPipelineWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/quotes-pipeline?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ClientsTopWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/clients-top?range=year");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task SuppliersTopWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/suppliers-top?range=year");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ProfitabilitySummaryWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/profitability-summary?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ProfitByBookingWidget_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/reports/widget/profitability-by-booking?range=month");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task ReportsPage_ContainsWidgetContainers()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/reports");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("#widgets-container");
        await response.AssertContainsAsync("hx-trigger=\"load\"");
    }

    // ── Access Control ──

    [Fact]
    public async Task ReportsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/reports");

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
