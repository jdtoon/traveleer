using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Clients;

public class ClientIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public ClientIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page ──

    [Fact]
    public async Task ClientsPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/clients");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
    }

    [Fact]
    public async Task ClientsPage_ContainsClientListTarget()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/clients");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("#client-list");
    }

    // ── Layer 2: Partial Isolation ──

    [Fact]
    public async Task ClientListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("table");
    }

    [Fact]
    public async Task ClientListPartial_ContainsSeedData()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/list");

        response.AssertSuccess();
        await response.AssertContainsAsync("Acacia Travel Group");
    }

    [Fact]
    public async Task ClientListPartial_UsesStandardDefaultPageSize()
    {
        var prefix = $"ClientPage-{Guid.NewGuid():N}";

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.Clients.Add(new saas.Modules.Clients.Entities.Client
                {
                    Id = Guid.NewGuid(),
                    Name = $"{prefix}-{index:D2}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/list?search={prefix}");

        response.AssertSuccess();
        await response.AssertContainsAsync($"{prefix}-01");
        await response.AssertContainsAsync($"{prefix}-12");
        await response.AssertDoesNotContainAsync($"{prefix}-13");
        await response.AssertContainsAsync("Page 1 of 2");
    }

    [Fact]
    public async Task ClientNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Client");
        await response.AssertContainsAsync("Save");
    }

    [Fact]
    public async Task ClientListPartial_SearchFiltersResults()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/list?search=Hoffmann");

        response.AssertSuccess();
        await response.AssertContainsAsync("Lena Hoffmann");
        await response.AssertDoesNotContainAsync("Acacia Travel Group");
    }

    // ── Layer 3: User Flow ──

    [Fact]
    public async Task ClientsPage_UserCanViewListAndOpenCreateForm()
    {
        // Step 1: Navigate to page
        var page = await _client.GetAsync($"/{TenantSlug}/clients");
        page.AssertSuccess();
        await page.AssertElementExistsAsync("#client-list");

        // Step 2: List partial loads
        var list = await _client.HtmxGetAsync($"/{TenantSlug}/clients/list");
        list.AssertSuccess();
        await list.AssertElementExistsAsync("table");

        // Step 3: Open create form
        var form = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        form.AssertSuccess();
        await form.AssertElementExistsAsync("dialog.modal");
    }

    [Fact]
    public async Task CreateClientForm_OnValidSubmit_PersistsToDatabase()
    {
        var uniqueName = $"DB-Client-{Guid.NewGuid():N}"[..20];
        var email = $"{uniqueName.ToLowerInvariant()}@example.test";

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Email"] = email,
            ["Company"] = "Test Co",
            ["Country"] = "United Kingdom"
        });

        response.AssertSuccess();
        response.AssertToast("Client created.");

        await using var db = OpenTenantDb();
        var client = await db.Clients.SingleAsync(c => c.Name == uniqueName);
        Assert.NotEqual(Guid.Empty, client.Id);
        Assert.Equal(email, client.Email);
        Assert.Equal("Test Co", client.Company);
        Assert.Equal("United Kingdom", client.Country);
        Assert.NotNull(client.CreatedAt);
    }

    [Fact]
    public async Task CreateClientForm_OnInvalidSubmit_ReturnsFormWithErrors()
    {
        // GET the form first
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        formResponse.AssertSuccess();

        // Submit with empty name (required field)
        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "",
            ["Email"] = "valid@email.test"
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    [Fact]
    public async Task CreateClientForm_DuplicateEmail_ReturnsFormWithError()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.test";
        var firstFormResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        await _client.SubmitFormAsync(firstFormResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "First Client",
            ["Email"] = email
        });

        var secondFormResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        var response = await _client.SubmitFormAsync(secondFormResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "Second Client",
            ["Email"] = email
        });

        response.AssertSuccess();
        await response.AssertContainsAsync("already exists");

        await using var db = OpenTenantDb();
        var count = await db.Clients.CountAsync(c => c.Email == email);
        Assert.Equal(1, count);
    }

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task EditClient_OnValidSubmit_UpdatesDatabase()
    {
        var uniqueName = $"Edit-Cl-{Guid.NewGuid():N}"[..18];
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["Company"] = "Original Co"
        });

        Guid clientId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.Clients.SingleAsync(c => c.Name == uniqueName);
            clientId = created.Id;
        }

        var editForm = await _client.HtmxGetAsync($"/{TenantSlug}/clients/edit/{clientId}");
        editForm.AssertSuccess();

        var updatedName = $"Upd-Cl-{Guid.NewGuid():N}"[..18];
        var response = await _client.SubmitFormAsync(editForm, "form", new Dictionary<string, string>
        {
            ["Name"] = updatedName,
            ["Company"] = "Updated Co",
            ["Country"] = "South Africa"
        });

        response.AssertSuccess();
        response.AssertToast("Client updated.");

        await using var verifyDb = OpenTenantDb();
        var updated = await verifyDb.Clients.SingleAsync(c => c.Id == clientId);
        Assert.Equal(updatedName, updated.Name);
        Assert.Equal("Updated Co", updated.Company);
        Assert.Equal("South Africa", updated.Country);
    }

    [Fact]
    public async Task DeleteClient_RemovesFromDatabase()
    {
        var uniqueName = $"Del-Cl-{Guid.NewGuid():N}"[..18];
        var createForm = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        await _client.SubmitFormAsync(createForm, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName
        });

        Guid clientId;
        await using (var db = OpenTenantDb())
        {
            var created = await db.Clients.SingleAsync(c => c.Name == uniqueName);
            clientId = created.Id;
        }

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/delete-confirm/{clientId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Client deleted.");

        await using var verifyDb = OpenTenantDb();
        Assert.False(await verifyDb.Clients.AnyAsync(c => c.Id == clientId));
    }

    // ── Access Control ──

    [Fact]
    public async Task ClientsPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/clients");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task ClientsList_WhenUnauthenticated_HtmxRedirectsToLogin()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.HtmxGetAsync($"/{TenantSlug}/clients/list");

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            response.AssertHeader("HX-Redirect", $"/{TenantSlug}/login?returnUrl=%2F{TenantSlug}%2Fclients%2Flist");
            return;
        }

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Unexpected status: {response.StatusCode}");
    }

    [Fact]
    public async Task ClientsCreate_WhenUnauthenticated_HtmxRedirectsToLogin()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient
            .AsHtmxRequest()
            .HtmxPostAsync($"/{TenantSlug}/clients/create", new Dictionary<string, string>
            {
                ["Name"] = "Test Client"
            });

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            response.AssertHeader("HX-Redirect", $"/{TenantSlug}/login?returnUrl=%2F{TenantSlug}%2Fclients%2Fcreate");
            return;
        }

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Unexpected status: {response.StatusCode}");
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
