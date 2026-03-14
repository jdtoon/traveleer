using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Clients.Entities;
using saas.Modules.Portal.Entities;
using saas.Modules.Quotes.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Portal;

public class ClientActionIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public ClientActionIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Admin Action Inbox ──

    [Fact]
    public async Task ActionIndex_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/portal/actions");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertContainsAsync("Client Actions");
    }

    [Fact]
    public async Task ActionList_WithActions_DisplaysItems()
    {
        await SeedClientActionAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/portal/actions/list");

        response.AssertSuccess();
        await response.AssertContainsAsync("Quote Accepted");
    }

    [Fact]
    public async Task ActionList_FilterByStatus_ReturnsMatching()
    {
        await SeedClientActionAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/portal/actions/list?status=Pending");

        response.AssertSuccess();
        await response.AssertContainsAsync("Pending");
    }

    [Fact]
    public async Task Acknowledge_UpdatesStatus()
    {
        var actionId = await SeedClientActionAsync();

        // Load the list which contains the acknowledge form with anti-forgery token
        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/portal/actions/list");
        listResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(
            listResponse,
            $"form[hx-post='/{TenantSlug}/portal/actions/acknowledge/{actionId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();

        await using var db = OpenTenantDb();
        var action = await db.ClientActions.FindAsync(actionId);
        Assert.Equal(ClientActionStatus.Acknowledged, action!.Status);
        Assert.NotNull(action.AcknowledgedByUserId);
    }

    [Fact]
    public async Task Dismiss_UpdatesStatus()
    {
        var actionId = await SeedClientActionAsync();

        // Load the list which contains the dismiss form with anti-forgery token
        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/portal/actions/list");
        listResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(
            listResponse,
            $"form[hx-post='/{TenantSlug}/portal/actions/dismiss/{actionId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();

        await using var db = OpenTenantDb();
        var action = await db.ClientActions.FindAsync(actionId);
        Assert.Equal(ClientActionStatus.Dismissed, action!.Status);
    }

    // ── Public Portal Actions ──

    [Fact]
    public async Task AcceptQuote_ValidToken_RecordsAction()
    {
        var (token, quoteId) = await SeedPortalContextAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.PostAsync(
            $"/portal/{TenantSlug}/{token}/quotes/{quoteId}/accept",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        response.AssertSuccess();
        await response.AssertContainsAsync("Quote Accepted");

        await using var db = OpenTenantDb();
        var action = await db.ClientActions
            .FirstOrDefaultAsync(a => a.EntityId == quoteId && a.ActionType == ClientActionType.AcceptQuote);
        Assert.NotNull(action);
        Assert.Equal(ClientActionStatus.Pending, action.Status);
    }

    [Fact]
    public async Task DeclineQuote_ValidToken_RecordsActionWithNotes()
    {
        var (token, quoteId) = await SeedPortalContextAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.PostAsync(
            $"/portal/{TenantSlug}/{token}/quotes/{quoteId}/decline",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["notes"] = "Too expensive" }));

        response.AssertSuccess();
        await response.AssertContainsAsync("Quote Declined");

        await using var db = OpenTenantDb();
        var action = await db.ClientActions
            .FirstOrDefaultAsync(a => a.EntityId == quoteId && a.ActionType == ClientActionType.DeclineQuote);
        Assert.NotNull(action);
        Assert.Equal("Too expensive", action.Notes);
    }

    [Fact]
    public async Task RequestChangeForm_ValidToken_RendersForm()
    {
        var (token, quoteId) = await SeedPortalContextAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync(
            $"/portal/{TenantSlug}/{token}/quotes/{quoteId}/change");

        response.AssertSuccess();
        await response.AssertContainsAsync("Request a Change");
    }

    [Fact]
    public async Task RequestChange_ValidToken_RecordsAction()
    {
        var (token, quoteId) = await SeedPortalContextAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.PostAsync(
            $"/portal/{TenantSlug}/{token}/quotes/{quoteId}/change",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["notes"] = "Different hotel please" }));

        response.AssertSuccess();
        await response.AssertContainsAsync("Change Request Submitted");

        await using var db = OpenTenantDb();
        var action = await db.ClientActions
            .FirstOrDefaultAsync(a => a.EntityId == quoteId && a.ActionType == ClientActionType.RequestChange);
        Assert.NotNull(action);
        Assert.Equal("Different hotel please", action.Notes);
    }

    [Fact]
    public async Task FeedbackForm_ValidToken_RendersForm()
    {
        var (token, _) = await SeedPortalContextAsync();
        var publicClient = _fixture.CreateClient();
        var bookingId = Guid.NewGuid();

        var response = await publicClient.GetAsync(
            $"/portal/{TenantSlug}/{token}/feedback/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Share Your Feedback");
    }

    [Fact]
    public async Task SubmitFeedback_ValidToken_RecordsAction()
    {
        var (token, _) = await SeedPortalContextAsync();
        var publicClient = _fixture.CreateClient();
        var bookingId = Guid.NewGuid();

        var response = await publicClient.PostAsync(
            $"/portal/{TenantSlug}/{token}/feedback/{bookingId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["notes"] = "Amazing trip!" }));

        response.AssertSuccess();
        await response.AssertContainsAsync("Feedback Submitted");

        await using var db = OpenTenantDb();
        var action = await db.ClientActions
            .FirstOrDefaultAsync(a => a.EntityId == bookingId && a.ActionType == ClientActionType.SubmitFeedback);
        Assert.NotNull(action);
        Assert.Equal("Amazing trip!", action.Notes);
    }

    [Fact]
    public async Task PortalAction_ExpiredToken_ShowsExpired()
    {
        var (token, quoteId) = await SeedPortalContextAsync();

        // Expire the link
        await using (var db = OpenTenantDb())
        {
            var link = await db.PortalLinks.FirstOrDefaultAsync(l => l.Token == token);
            link!.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var publicClient = _fixture.CreateClient();
        var response = await publicClient.PostAsync(
            $"/portal/{TenantSlug}/{token}/quotes/{quoteId}/accept",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        response.AssertSuccess();
        await response.AssertContainsAsync("Unavailable");
    }

    // ── Access Control ──

    [Fact]
    public async Task ActionIndex_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/portal/actions");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // ========== HELPERS ==========

    private async Task<Guid> SeedClientActionAsync()
    {
        await using var db = OpenTenantDb();
        var client = await db.Clients.FirstOrDefaultAsync()
            ?? new Client { Name = "Action Test Client", Email = "action@test.com", CreatedAt = DateTime.UtcNow };
        if (client.Id == Guid.Empty)
        {
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }

        var action = new ClientAction
        {
            ClientId = client.Id,
            ActionType = ClientActionType.AcceptQuote,
            EntityType = "Quote",
            EntityId = Guid.NewGuid(),
            Status = ClientActionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        db.ClientActions.Add(action);
        await db.SaveChangesAsync();
        return action.Id;
    }

    private async Task<(string Token, Guid QuoteId)> SeedPortalContextAsync()
    {
        await using var db = OpenTenantDb();

        var client = await db.Clients.FirstOrDefaultAsync();
        if (client is null)
        {
            client = new Client { Name = "Portal Action Client", Email = "portalaction@test.com", CreatedAt = DateTime.UtcNow };
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }

        var quote = new Quote
        {
            ClientId = client.Id,
            ReferenceNumber = $"QT-ACT-{Guid.NewGuid().ToString()[..6]}",
            ClientName = client.Name,
            Status = QuoteStatus.Sent,
            CreatedAt = DateTime.UtcNow
        };
        db.Quotes.Add(quote);

        var token = $"action-test-{Guid.NewGuid():N}";
        var link = new PortalLink
        {
            ClientId = client.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Scope = PortalLinkScope.Full,
            CreatedByUserId = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        db.PortalLinks.Add(link);
        await db.SaveChangesAsync();
        return (token, quote.Id);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
