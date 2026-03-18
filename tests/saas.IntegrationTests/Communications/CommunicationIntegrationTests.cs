using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Communications.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Communications;

public class CommunicationIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public CommunicationIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Empty State ──

    [Fact]
    public async Task BookingCommsPartial_RendersEmptyState()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/comms/booking/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No communications logged yet.");
    }

    [Fact]
    public async Task ClientCommsPartial_RendersEmptyStateForNewClient()
    {
        var clientId = await SeedFreshClientAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/comms/client/{clientId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No communications logged yet.");
    }

    // ── New Form ──

    [Fact]
    public async Task NewForm_RendersModalWithFields()
    {
        var clientId = await GetFirstClientIdAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/comms/new?clientId={clientId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Log Communication");
        await response.AssertContainsAsync("Channel");
        await response.AssertContainsAsync("Direction");
        await response.AssertContainsAsync("Content");
    }

    [Fact]
    public async Task NewForm_ContainsHiddenContextFields()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/comms/new?bookingId={bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync(bookingId.ToString());
    }

    // ── Create ──

    [Fact]
    public async Task Create_PersistsEntryAndReturnsToastAndTrigger()
    {
        var clientId = await SeedFreshClientAsync();
        var uniqueSubject = $"Phone-{Guid.NewGuid():N}"[..20];

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/comms/new?clientId={clientId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["ClientId"] = clientId.ToString(),
            ["Channel"] = "1",
            ["Direction"] = "0",
            ["Subject"] = uniqueSubject,
            ["Content"] = "Discussed itinerary changes for summer trip.",
            ["OccurredAt"] = "2025-03-10T14:00"
        });

        response.AssertSuccess();
        response.AssertToast("Communication logged.");
        response.AssertTrigger("comms.refresh");

        await using var db = OpenTenantDb();
        var entry = await db.CommunicationEntries.SingleOrDefaultAsync(e => e.ClientId == clientId && e.Subject == uniqueSubject);
        Assert.NotNull(entry);
        Assert.Equal(CommunicationChannel.Phone, entry!.Channel);
        Assert.Equal(CommunicationDirection.Inbound, entry.Direction);
    }

    // ── Edit Form ──

    [Fact]
    public async Task EditForm_LoadsCorrectly()
    {
        var entryId = await SeedCommunicationEntryAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/comms/edit/{entryId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Edit Communication");
        await response.AssertContainsAsync("Test communication content");
    }

    // ── Update ──

    [Fact]
    public async Task Update_ModifiesEntryAndReturnsToast()
    {
        var entryId = await SeedCommunicationEntryAsync();

        var editResponse = await _client.HtmxGetAsync($"/{TenantSlug}/comms/edit/{entryId}");
        editResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(editResponse, "form", new Dictionary<string, string>
        {
            ["Channel"] = "2",
            ["Direction"] = "1",
            ["Subject"] = "Updated subject",
            ["Content"] = "Updated communication content",
            ["OccurredAt"] = "2025-06-01T12:00"
        });

        response.AssertSuccess();
        response.AssertToast("Communication updated.");
        response.AssertTrigger("comms.refresh");

        await using var db = OpenTenantDb();
        var entry = await db.CommunicationEntries.FindAsync(entryId);
        Assert.NotNull(entry);
        Assert.Equal(CommunicationChannel.WhatsApp, entry!.Channel);
        Assert.Equal("Updated communication content", entry.Content);
    }

    // ── Delete ──

    [Fact]
    public async Task Delete_RemovesEntryAndReturnsToast()
    {
        var (entryId, clientId) = await SeedCommunicationEntryWithClientAsync();

        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/comms/client/{clientId}");
        listResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(
            listResponse,
            $"form[hx-post='/{TenantSlug}/comms/delete/{entryId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Communication deleted.");
        response.AssertTrigger("comms.refresh");

        await using var db = OpenTenantDb();
        var entry = await db.CommunicationEntries.FindAsync(entryId);
        Assert.Null(entry);
    }

    // ── Booking Detail Integration ──

    [Fact]
    public async Task BookingDetails_ShowsCommunicationsSection()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.GetAsync($"/{TenantSlug}/bookings/details/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Communications");
        await response.AssertElementExistsAsync("#booking-communications");
    }

    // ── List Shows Entry ──

    [Fact]
    public async Task BookingCommsPartial_ShowsSeededEntry()
    {
        var bookingId = await SeedBookingAsync();
        await SeedCommunicationEntryForBookingAsync(bookingId);

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/comms/booking/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Booking communication");
    }

    [Fact]
    public async Task ClientCommsPartial_WhenMoreThanOnePage_PaginatesResults()
    {
        var clientId = await SeedFreshClientAsync();

        await using (var db = OpenTenantDb())
        {
            var baseTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var index = 1; index <= 21; index++)
            {
                db.CommunicationEntries.Add(new CommunicationEntry
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    Channel = CommunicationChannel.Email,
                    Direction = CommunicationDirection.Outbound,
                    Subject = $"Paged comm {index:D2}",
                    Content = $"Paged client communication {index:D2}",
                    LoggedByUserId = "seed-user",
                    OccurredAt = baseTime.AddMinutes(index),
                    CreatedAt = baseTime.AddMinutes(index)
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await _client.HtmxGetAsync($"/{TenantSlug}/comms/client/{clientId}");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync("Paged client communication 21");
        await firstPage.AssertDoesNotContainAsync("Paged client communication 01");
        await firstPage.AssertContainsAsync("Load More");

        var secondPage = await _client.HtmxGetAsync($"/{TenantSlug}/comms/client/{clientId}?page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync("Paged client communication 01");
        await secondPage.AssertDoesNotContainAsync("Paged client communication 21");
    }

    // ── Auth ──

    [Fact]
    public async Task CommsRoutes_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var clientId = await GetFirstClientIdAsync();

        var response = await publicClient.GetAsync($"/{TenantSlug}/comms/client/{clientId}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // ── Helpers ──

    private async Task<Guid> SeedBookingAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-CM-{Guid.NewGuid():N}"[..13],
            ClientId = clientId,
            Pax = 2,
            TravelStartDate = new DateOnly(2026, 8, 1),
            TravelEndDate = new DateOnly(2026, 8, 10),
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            TotalSelling = 3000m,
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<Guid> SeedFreshClientAsync()
    {
        await using var db = OpenTenantDb();
        var client = new saas.Modules.Clients.Entities.Client
        {
            Name = $"CommTest-{Guid.NewGuid():N}"[..20],
            CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> GetFirstClientIdAsync()
    {
        await using var db = OpenTenantDb();
        return await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
    }

    private async Task<Guid> SeedCommunicationEntryAsync()
    {
        var (entryId, _) = await SeedCommunicationEntryWithClientAsync();
        return entryId;
    }

    private async Task<(Guid EntryId, Guid ClientId)> SeedCommunicationEntryWithClientAsync()
    {
        var clientId = await SeedFreshClientAsync();
        await using var db = OpenTenantDb();
        var entry = new CommunicationEntry
        {
            ClientId = clientId,
            Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound,
            Subject = "Test subject",
            Content = "Test communication content",
            OccurredAt = DateTime.UtcNow,
            LoggedByUserId = "seed-user",
            CreatedAt = DateTime.UtcNow
        };
        db.CommunicationEntries.Add(entry);
        await db.SaveChangesAsync();
        return (entry.Id, clientId);
    }

    private async Task SeedCommunicationEntryForBookingAsync(Guid bookingId)
    {
        await using var db = OpenTenantDb();
        db.CommunicationEntries.Add(new CommunicationEntry
        {
            BookingId = bookingId,
            Channel = CommunicationChannel.Phone,
            Direction = CommunicationDirection.Inbound,
            Subject = "Booking communication",
            Content = "Discussed the booking details",
            OccurredAt = DateTime.UtcNow,
            LoggedByUserId = "seed-user",
            CreatedAt = DateTime.UtcNow
        });
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
