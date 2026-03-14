using Microsoft.Extensions.DependencyInjection;
using saas.Data.Audit;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Audit.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Audit;

public class AuditDashboardIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public AuditDashboardIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task AuditPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/audit");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertContainsAsync("Audit Log");
        await response.AssertElementExistsAsync("#audit-list");
    }

    [Fact]
    public async Task AuditListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/audit/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task AuditPage_ShowsAuditEntries()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/audit");

        response.AssertSuccess();
        await response.AssertContainsAsync("Booking");
    }

    [Fact]
    public async Task AuditPage_FilterByEntityType()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/audit?entity=Client");

        response.AssertSuccess();
        await response.AssertContainsAsync("Client");
    }

    [Fact]
    public async Task AuditPage_FilterByAction()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/audit?action=Created");

        response.AssertSuccess();
        await response.AssertContainsAsync("Created");
    }

    [Fact]
    public async Task AuditDetailModal_ShowsFieldChanges()
    {
        long entryId;
        using (var scope = _fixture.AdminTenantFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            entryId = db.AuditEntries
                .Where(e => e.TenantSlug == TenantSlug && e.OldValues != null && e.NewValues != null)
                .OrderByDescending(e => e.Timestamp)
                .Select(e => e.Id)
                .First();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/audit/details/{entryId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog");
    }

    [Fact]
    public async Task AuditDetailModal_ReturnsNotFoundForOtherTenant()
    {
        long otherEntryId;
        using (var scope = _fixture.AdminTenantFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var entry = new AuditEntry
            {
                Source = "Tenant", TenantSlug = "other-tenant", EntityType = "Secret", EntityId = "S001",
                Action = "Created", UserEmail = "hacker@evil.com",
                Timestamp = DateTime.UtcNow, IpAddress = "127.0.0.1"
            };
            db.AuditEntries.Add(entry);
            db.SaveChanges();
            otherEntryId = entry.Id;
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/audit/details/{otherEntryId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuditPage_MemberWithoutPermission_ReturnsForbidden()
    {
        var memberClient = _fixture.CreateTenantMemberClient(TenantSlug);

        var response = await memberClient.GetAsync($"/{TenantSlug}/audit");

        // Member does not have audit.read permission by default
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            response.StatusCode == System.Net.HttpStatusCode.NotFound ||
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Expected Forbidden, NotFound, or Redirect but got {response.StatusCode}");
    }
}
