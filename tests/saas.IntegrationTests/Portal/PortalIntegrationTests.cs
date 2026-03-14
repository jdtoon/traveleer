using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Portal.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Portal;

public class PortalIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public PortalIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page Load ──

    [Fact]
    public async Task PortalLinksPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/portal/links");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertContainsAsync("Portal Links");
    }

    // ── Layer 2: Admin Link Management ──

    [Fact]
    public async Task NewLinkForm_RendersModal()
    {
        var clientId = await GetFirstClientIdAsync();
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/portal/links/new/{clientId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Create Portal Link");
    }

    [Fact]
    public async Task CreateLink_PersistsToDatabase()
    {
        var clientId = await GetFirstClientIdAsync();
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/portal/links/new/{clientId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["ClientId"] = clientId.ToString(),
            ["Scope"] = "0",
            ["ExpiryDays"] = "30"
        });

        response.AssertSuccess();

        await using var db = OpenTenantDb();
        var link = await db.PortalLinks.FirstOrDefaultAsync(l => l.ClientId == clientId);
        Assert.NotNull(link);
        Assert.False(link.IsRevoked);
        Assert.NotEmpty(link.Token);
    }

    [Fact]
    public async Task ClientLinks_ReturnsLinksForClient()
    {
        var clientId = await GetFirstClientIdAsync();

        // Seed a link directly
        await using (var db = OpenTenantDb())
        {
            db.PortalLinks.Add(new PortalLink
            {
                ClientId = clientId,
                Token = $"links-test-{Guid.NewGuid():N}",
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                Scope = PortalLinkScope.Full,
                CreatedByUserId = "test-user",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/portal/links/client/{clientId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Active");
    }

    // ── Layer 3: Public Portal Access ──

    [Fact]
    public async Task PortalEntry_ValidToken_RedirectsToDashboard()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task PortalDashboard_ValidToken_RendersContent()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/dashboard");

        response.AssertSuccess();
        await response.AssertContainsAsync("Welcome");
        await response.AssertContainsAsync("Bookings");
    }

    [Fact]
    public async Task PortalDashboard_ExpiredToken_ShowsExpiredPage()
    {
        var (linkId, token) = await SeedPortalLinkWithTokenAsync();

        // Expire the link
        await using (var db = OpenTenantDb())
        {
            var link = await db.PortalLinks.FindAsync(linkId);
            link!.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/dashboard");

        response.AssertSuccess();
        await response.AssertContainsAsync("Link Unavailable");
    }

    [Fact]
    public async Task PortalBookings_ValidToken_RendersList()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/bookings");

        response.AssertSuccess();
        await response.AssertContainsAsync("Your Bookings");
    }

    [Fact]
    public async Task PortalQuotes_ValidToken_RendersList()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/quotes");

        response.AssertSuccess();
        await response.AssertContainsAsync("Your Quotes");
    }

    [Fact]
    public async Task PortalDocuments_ValidToken_RendersList()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/documents");

        response.AssertSuccess();
        await response.AssertContainsAsync("Your Documents");
    }

    [Fact]
    public async Task PortalBookings_QuoteOnlyScope_Returns404()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync(PortalLinkScope.QuoteOnly);
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/bookings");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PortalQuotes_BookingOnlyScope_Returns404()
    {
        var (_, token) = await SeedPortalLinkWithTokenAsync(PortalLinkScope.BookingOnly);
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/quotes");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Access Control ──

    [Fact]
    public async Task PortalLinksPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/portal/links");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // ========== HELPERS ==========

    private async Task<Guid> GetFirstClientIdAsync()
    {
        await using var db = OpenTenantDb();
        var client = await db.Clients.FirstOrDefaultAsync();
        if (client is not null) return client.Id;

        // Seed a client if none exists
        client = new Client { Name = "Portal Test Client", Email = "portal@test.com", CreatedAt = DateTime.UtcNow };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedPortalLinkAsync()
    {
        var clientId = await GetFirstClientIdAsync();
        await using var db = OpenTenantDb();

        var link = new PortalLink
        {
            ClientId = clientId,
            Token = $"test-token-{Guid.NewGuid():N}",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Scope = PortalLinkScope.Full,
            CreatedByUserId = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        db.PortalLinks.Add(link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    private async Task<(Guid LinkId, string Token)> SeedPortalLinkWithTokenAsync(PortalLinkScope scope = PortalLinkScope.Full)
    {
        var clientId = await GetFirstClientIdAsync();
        await using var db = OpenTenantDb();

        var token = $"integ-{Guid.NewGuid():N}";
        var link = new PortalLink
        {
            ClientId = clientId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Scope = scope,
            CreatedByUserId = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        db.PortalLinks.Add(link);
        await db.SaveChangesAsync();
        return (link.Id, token);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
