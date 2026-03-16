using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Itineraries.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Itineraries;

public class ItineraryIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public ItineraryIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page Load ──

    [Fact]
    public async Task ItinerariesPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/itineraries");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertContainsAsync("Itineraries");
    }

    [Fact]
    public async Task ItineraryDetailsPage_AfterCreate_RendersFullLayout()
    {
        var id = await SeedItineraryAsync();

        var response = await _client.GetAsync($"/{TenantSlug}/itineraries/details/{id}");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync($"a[href='/{TenantSlug}/itineraries']");
        await response.AssertContainsAsync("Back to Itineraries");
    }

    // ── Layer 2: Partial Isolation ──

    [Fact]
    public async Task ItineraryListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    [Fact]
    public async Task ItineraryListPartial_RendersClientDetailLinks()
    {
        var itineraryId = await SeedItineraryAsync();

        string seededTitle;
        await using (var db = OpenTenantDb())
        {
            var itinerary = await db.Itineraries.FirstAsync(i => i.Id == itineraryId);
            var client = await db.Clients.OrderBy(c => c.Name).FirstAsync();
            itinerary.ClientId = client.Id;
            await db.SaveChangesAsync();
            seededTitle = itinerary.Title;
        }

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/list?search={Uri.EscapeDataString(seededTitle)}");

        response.AssertSuccess();
        await response.AssertContainsAsync($"hx-get=\"/{TenantSlug}/clients/details/");
    }

    [Fact]
    public async Task ItineraryListPartial_WhenMoreThanOnePage_PaginatesResults()
    {
        var prefix = $"PagedItin-{Guid.NewGuid():N}";

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.Itineraries.Add(new Itinerary
                {
                    Id = Guid.NewGuid(),
                    Title = $"{prefix}-{index:D2}",
                    Status = ItineraryStatus.Draft,
                    CreatedAt = DateTime.UtcNow.AddDays(-(14 - index))
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/list?search={prefix}");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync($"{prefix}-13");
        await firstPage.AssertContainsAsync($"{prefix}-02");
        await firstPage.AssertDoesNotContainAsync($"{prefix}-01");
        await firstPage.AssertContainsAsync("Next");

        var secondPage = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/list?search={prefix}&page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync($"{prefix}-01");
        await secondPage.AssertDoesNotContainAsync($"{prefix}-13");
    }

    [Fact]
    public async Task ItineraryDayListPartial_RendersWithoutLayout()
    {
        var id = await SeedItineraryAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/days/{id}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
    }

    // ── Layer 3: User Flow ──

    [Fact]
    public async Task ItineraryNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Itinerary");
    }

    [Fact]
    public async Task CreateItinerary_OnInvalidSubmit_RerendersForm()
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/new");
        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Title"] = ""
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task CreateItinerary_OnValidSubmit_PersistsToDatabase()
    {
        var uniqueTitle = $"Itin-{Guid.NewGuid():N}"[..16];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Title"] = uniqueTitle,
            ["Notes"] = "Test notes",
            ["PublicNotes"] = "Public test notes",
            ["TravelStartDate"] = "2025-06-01",
            ["TravelEndDate"] = "2025-06-07"
        });

        response.AssertSuccess();
        response.AssertToast("Itinerary created.");

        await using var db = OpenTenantDb();
        var itinerary = await db.Itineraries.SingleAsync(i => i.Title == uniqueTitle);
        Assert.NotEqual(Guid.Empty, itinerary.Id);
        Assert.Equal("Test notes", itinerary.Notes);
        Assert.Equal(ItineraryStatus.Draft, itinerary.Status);
    }

    [Fact]
    public async Task EditItinerary_OnValidSubmit_UpdatesDatabase()
    {
        var id = await SeedItineraryAsync();

        var editForm = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/edit/{id}");
        editForm.AssertSuccess();

        var updatedTitle = $"Updated-{Guid.NewGuid():N}"[..16];
        var response = await _client.SubmitFormAsync(editForm, "form", new Dictionary<string, string>
        {
            ["Title"] = updatedTitle,
            ["Notes"] = "Updated notes"
        });

        response.AssertSuccess();
        response.AssertToast("Itinerary updated.");

        await using var db = OpenTenantDb();
        var itinerary = await db.Itineraries.SingleAsync(i => i.Id == id);
        Assert.Equal(updatedTitle, itinerary.Title);
        Assert.Equal("Updated notes", itinerary.Notes);
    }

    [Fact]
    public async Task DeleteItinerary_RemovesFromDatabase()
    {
        var id = await SeedItineraryAsync();

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/delete-confirm/{id}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());
        response.AssertSuccess();

        await using var db = OpenTenantDb();
        Assert.False(await db.Itineraries.AnyAsync(i => i.Id == id));
    }

    [Fact]
    public async Task CreateDay_OnValidSubmit_PersistsToDatabase()
    {
        var itineraryId = await SeedItineraryAsync();

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/days/new/{itineraryId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["DayNumber"] = "1",
            ["Title"] = "Arrival Day",
            ["Description"] = "Check-in and welcome",
            ["SortOrder"] = "10"
        });

        response.AssertSuccess();
        response.AssertToast("Day added.");

        await using var db = OpenTenantDb();
        var day = await db.ItineraryDays.SingleAsync(d => d.ItineraryId == itineraryId && d.DayNumber == 1);
        Assert.Equal("Arrival Day", day.Title);
        Assert.Equal("Check-in and welcome", day.Description);
    }

    [Fact]
    public async Task DeleteDay_RemovesFromDatabase()
    {
        var itineraryId = await SeedItineraryAsync();
        var dayId = await SeedDayAsync(itineraryId);

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/days/delete-confirm/{dayId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());
        response.AssertSuccess();
        response.AssertToast("Day removed.");

        await using var db = OpenTenantDb();
        Assert.False(await db.ItineraryDays.AnyAsync(d => d.Id == dayId));
    }

    [Fact]
    public async Task CreateItem_OnValidSubmit_PersistsToDatabase()
    {
        var itineraryId = await SeedItineraryAsync();
        var dayId = await SeedDayAsync(itineraryId);

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/items/new/{dayId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Title"] = "Game Drive",
            ["Description"] = "Morning safari",
            ["StartTime"] = "06:00",
            ["EndTime"] = "10:00",
            ["SortOrder"] = "10"
        });

        response.AssertSuccess();
        response.AssertToast("Item added.");

        await using var db = OpenTenantDb();
        var item = await db.ItineraryItems.SingleAsync(i => i.ItineraryDayId == dayId && i.Title == "Game Drive");
        Assert.Equal("Morning safari", item.Description);
    }

    [Fact]
    public async Task DeleteItem_RemovesFromDatabase()
    {
        var itineraryId = await SeedItineraryAsync();
        var dayId = await SeedDayAsync(itineraryId);
        var itemId = await SeedItemAsync(dayId);

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/items/delete-confirm/{itemId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());
        response.AssertSuccess();
        response.AssertToast("Item removed.");

        await using var db = OpenTenantDb();
        Assert.False(await db.ItineraryItems.AnyAsync(i => i.Id == itemId));
    }

    // ── Access Control ──

    [Fact]
    public async Task ItinerariesPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/itineraries");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // ========== HELPERS ==========

    private async Task<Guid> SeedItineraryAsync()
    {
        var uniqueTitle = $"Itin-{Guid.NewGuid():N}"[..16];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/new");
        await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Title"] = uniqueTitle,
            ["TravelStartDate"] = "2025-06-01",
            ["TravelEndDate"] = "2025-06-07"
        });

        await using var db = OpenTenantDb();
        return (await db.Itineraries.SingleAsync(i => i.Title == uniqueTitle)).Id;
    }

    private async Task<Guid> SeedDayAsync(Guid itineraryId)
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/days/new/{itineraryId}");
        await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["DayNumber"] = "1",
            ["Title"] = $"Day-{Guid.NewGuid():N}"[..12],
            ["SortOrder"] = "10"
        });

        await using var db = OpenTenantDb();
        return (await db.ItineraryDays.Where(d => d.ItineraryId == itineraryId)
            .OrderByDescending(d => d.CreatedAt).FirstAsync()).Id;
    }

    private async Task<Guid> SeedItemAsync(Guid dayId)
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/itineraries/items/new/{dayId}");
        await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Title"] = $"Item-{Guid.NewGuid():N}"[..12],
            ["SortOrder"] = "10"
        });

        await using var db = OpenTenantDb();
        return (await db.ItineraryItems.Where(i => i.ItineraryDayId == dayId)
            .OrderByDescending(i => i.CreatedAt).FirstAsync()).Id;
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
