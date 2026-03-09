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
    public async Task CreateClientForm_OnValidSubmit_ClosesModalAndRefreshes()
    {
        // GET the form to obtain antiforgery token
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        formResponse.AssertSuccess();

        // Submit via SubmitFormAsync which extracts antiforgery token automatically
        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "Integration Test Client",
            ["Email"] = $"inttest-{Guid.NewGuid():N}@example.test",
            ["Company"] = "Test Co",
            ["Country"] = "United Kingdom"
        });

        response.AssertSuccess();
        response.AssertToast("Client created.");
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
        // First create a client
        var email = $"dup-{Guid.NewGuid():N}@example.test";
        var firstFormResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        await _client.SubmitFormAsync(firstFormResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "First Client",
            ["Email"] = email
        });

        // Try to create another with the same email
        var secondFormResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
        var response = await _client.SubmitFormAsync(secondFormResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "Second Client",
            ["Email"] = email
        });

        response.AssertSuccess();
        await response.AssertContainsAsync("already exists");
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
}
