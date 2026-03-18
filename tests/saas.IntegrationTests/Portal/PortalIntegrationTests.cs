using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Portal.Entities;
using saas.Modules.Quotes.Entities;
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
    public async Task RevokeLink_FromPortalLinksPage_UpdatesDatabaseAndReturnsUpdatedList()
    {
        var linkId = await SeedPortalLinkAsync();

        var page = await _client.GetAsync($"/{TenantSlug}/portal/links");
        page.AssertSuccess();
        await page.AssertElementExistsAsync($"form[action='/{TenantSlug}/portal/links/revoke/{linkId}']");

        var response = await _client.SubmitFormAsync(
            page,
            $"form[action='/{TenantSlug}/portal/links/revoke/{linkId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();
        await response.AssertContainsAsync("Revoked");

        await using var db = OpenTenantDb();
        var link = await db.PortalLinks.FindAsync(linkId);
        Assert.NotNull(link);
        Assert.True(link!.IsRevoked);
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
        await response.AssertContainsAsync("/_content/Swap.Htmx/js/swap.client.js");
        await response.AssertDoesNotContainAsync("/_content/Swap.Htmx/js/swap.js");
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
    public async Task PortalBookings_WhenMoreThanOnePage_PaginatesResults()
    {
        var (clientId, token) = await SeedDedicatedPortalContextAsync();
        var prefix = $"PORT-BK-{Guid.NewGuid():N}";

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.Bookings.Add(new Booking
                {
                    Id = Guid.NewGuid(),
                    BookingRef = $"{prefix}-{index:D2}",
                    ClientId = clientId,
                    Pax = 2,
                    TravelStartDate = new DateOnly(2026, 1, 1).AddDays(index),
                    TravelEndDate = new DateOnly(2026, 1, 2).AddDays(index),
                    CostCurrencyCode = "USD",
                    SellingCurrencyCode = "USD",
                    TotalSelling = 1000m + index,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        var publicClient = _fixture.CreateClient();
        var firstPage = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/bookings");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync($"{prefix}-13");
        await firstPage.AssertDoesNotContainAsync($"{prefix}-01");
        await firstPage.AssertContainsAsync("Page 1 of 2");

        var secondPage = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/bookings?page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync($"{prefix}-01");
        await secondPage.AssertContainsAsync("Page 2 of 2");
    }

    [Fact]
    public async Task PortalQuotes_WhenMoreThanOnePage_PaginatesResults()
    {
        var (clientId, token) = await SeedDedicatedPortalContextAsync();
        var prefix = $"PORT-QT-{Guid.NewGuid():N}";

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.Quotes.Add(new Quote
                {
                    Id = Guid.NewGuid(),
                    ReferenceNumber = $"{prefix}-{index:D2}",
                    ClientId = clientId,
                    ClientName = "Portal Pagination Client",
                    OutputCurrencyCode = "USD",
                    Status = QuoteStatus.Sent,
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)
                });
            }

            await db.SaveChangesAsync();
        }

        var publicClient = _fixture.CreateClient();
        var firstPage = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/quotes");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync($"{prefix}-13");
        await firstPage.AssertDoesNotContainAsync($"{prefix}-01");
        await firstPage.AssertContainsAsync("Page 1 of 2");

        var secondPage = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/quotes?page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync($"{prefix}-01");
        await secondPage.AssertContainsAsync("Page 2 of 2");
    }

    [Fact]
    public async Task PortalDocuments_WhenMoreThanOnePage_PaginatesResults()
    {
        var (clientId, token) = await SeedDedicatedPortalContextAsync();

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.Documents.Add(new Document
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    FileName = $"portal-doc-{index:D2}.pdf",
                    ContentType = "application/pdf",
                    FileSize = 2048 + index,
                    StorageKey = $"portal/docs/{Guid.NewGuid():N}",
                    DocumentType = DocumentType.Other,
                    CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)
                });
            }

            await db.SaveChangesAsync();
        }

        var publicClient = _fixture.CreateClient();
        var firstPage = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/documents");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync("portal-doc-13.pdf");
        await firstPage.AssertDoesNotContainAsync("portal-doc-01.pdf");
        await firstPage.AssertContainsAsync("Page 1 of 2");

        var secondPage = await publicClient.GetAsync($"/portal/{TenantSlug}/{token}/documents?page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync("portal-doc-01.pdf");
        await secondPage.AssertContainsAsync("Page 2 of 2");
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

    private async Task<(Guid ClientId, string Token)> SeedDedicatedPortalContextAsync(PortalLinkScope scope = PortalLinkScope.Full)
    {
        await using var db = OpenTenantDb();

        var client = new Client
        {
            Id = Guid.NewGuid(),
            Name = $"Portal Pagination Client {Guid.NewGuid():N}"[..30],
            Email = $"portal-pagination-{Guid.NewGuid():N}@test.local",
            CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);

        var token = $"portal-page-{Guid.NewGuid():N}";
        db.PortalLinks.Add(new PortalLink
        {
            ClientId = client.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Scope = scope,
            CreatedByUserId = "test-user",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (client.Id, token);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
