using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Portal.DTOs;
using saas.Modules.Portal.Entities;
using saas.Modules.Portal.Services;
using saas.Modules.Quotes.Entities;
using Xunit;

namespace saas.Tests.Modules.Portal;

public class PortalServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private PortalService _service = null!;
    private Client _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _client = new Client
        {
            Name = "John Doe",
            Email = "john@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _db.Clients.Add(_client);

        // Add sample booking
        _db.Bookings.Add(new Booking
        {
            ClientId = _client.Id,
            BookingRef = "BK-001",
            Status = BookingStatus.Confirmed,
            TravelStartDate = new DateOnly(2025, 7, 1),
            TravelEndDate = new DateOnly(2025, 7, 10),
            CreatedAt = DateTime.UtcNow
        });

        // Add sample quote
        _db.Quotes.Add(new Quote
        {
            ClientId = _client.Id,
            ReferenceNumber = "QT-001",
            ClientName = "John Doe",
            Status = QuoteStatus.Sent,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _service = new PortalService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ========== CREATE LINK ==========

    [Fact]
    public async Task CreateLinkAsync_CreatesLinkWithToken()
    {
        var dto = new CreatePortalLinkDto
        {
            ClientId = _client.Id,
            Scope = PortalLinkScope.Full,
            ExpiryDays = 30
        };

        var link = await _service.CreateLinkAsync(dto, "user-1");

        Assert.NotNull(link);
        Assert.NotEmpty(link.Token);
        Assert.Equal(_client.Id, link.ClientId);
        Assert.Equal(PortalLinkScope.Full, link.Scope);
        Assert.Equal("user-1", link.CreatedByUserId);
        Assert.False(link.IsRevoked);
    }

    [Fact]
    public async Task CreateLinkAsync_SetsExpiryCorrectly()
    {
        var dto = new CreatePortalLinkDto
        {
            ClientId = _client.Id,
            ExpiryDays = 7
        };

        var link = await _service.CreateLinkAsync(dto, "user-1");

        var expectedExpiry = DateTime.UtcNow.AddDays(7);
        Assert.True(Math.Abs((link.ExpiresAt - expectedExpiry).TotalMinutes) < 1);
    }

    [Fact]
    public async Task CreateLinkAsync_GeneratesUniqueTokens()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };

        var link1 = await _service.CreateLinkAsync(dto, "user-1");
        var link2 = await _service.CreateLinkAsync(dto, "user-1");

        Assert.NotEqual(link1.Token, link2.Token);
    }

    [Fact]
    public async Task CreateLinkAsync_WithScopedEntity_StoresScope()
    {
        var bookingId = Guid.NewGuid();
        var dto = new CreatePortalLinkDto
        {
            ClientId = _client.Id,
            Scope = PortalLinkScope.BookingOnly,
            ScopedEntityId = bookingId,
            ExpiryDays = 14
        };

        var link = await _service.CreateLinkAsync(dto, "user-1");

        Assert.Equal(PortalLinkScope.BookingOnly, link.Scope);
        Assert.Equal(bookingId, link.ScopedEntityId);
    }

    // ========== VALIDATE TOKEN ==========

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsLink()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };
        var created = await _service.CreateLinkAsync(dto, "user-1");

        var link = await _service.ValidateTokenAsync(created.Token);

        Assert.NotNull(link);
        Assert.Equal(created.Id, link.Id);
        Assert.NotNull(link.LastAccessedAt);
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsNull()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 1 };
        var created = await _service.CreateLinkAsync(dto, "user-1");

        // Manually expire the link
        created.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var link = await _service.ValidateTokenAsync(created.Token);
        Assert.Null(link);
    }

    [Fact]
    public async Task ValidateTokenAsync_RevokedToken_ReturnsNull()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };
        var created = await _service.CreateLinkAsync(dto, "user-1");

        await _service.RevokeAsync(created.Id);

        var link = await _service.ValidateTokenAsync(created.Token);
        Assert.Null(link);
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsNull()
    {
        var link = await _service.ValidateTokenAsync("nonexistent-token");
        Assert.Null(link);
    }

    // ========== REVOKE ==========

    [Fact]
    public async Task RevokeAsync_SetsIsRevoked()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };
        var created = await _service.CreateLinkAsync(dto, "user-1");

        await _service.RevokeAsync(created.Id);

        var link = await _db.PortalLinks.FindAsync(created.Id);
        Assert.True(link!.IsRevoked);
    }

    [Fact]
    public async Task RevokeAsync_NonExistentId_DoesNotThrow()
    {
        await _service.RevokeAsync(Guid.NewGuid()); // should not throw
    }

    // ========== GET LINKS ==========

    [Fact]
    public async Task GetLinksAsync_ReturnsAllLinks()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };
        await _service.CreateLinkAsync(dto, "user-1");
        await _service.CreateLinkAsync(dto, "user-1");

        var links = await _service.GetLinksAsync();

        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task GetLinksAsync_FilterByClient_ReturnsOnlyMatching()
    {
        var otherClient = new Client { Name = "Jane", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(otherClient);
        await _db.SaveChangesAsync();

        await _service.CreateLinkAsync(new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 }, "user-1");
        await _service.CreateLinkAsync(new CreatePortalLinkDto { ClientId = otherClient.Id, ExpiryDays = 30 }, "user-1");

        var links = await _service.GetLinksAsync(_client.Id);

        Assert.Single(links);
        Assert.Equal(_client.Name, links[0].ClientName);
    }

    // ========== SESSION ==========

    [Fact]
    public async Task CreateSessionAsync_CreatesSession()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };
        var link = await _service.CreateLinkAsync(dto, "user-1");

        var session = await _service.CreateSessionAsync(link.Id, _client.Id, "127.0.0.1");

        Assert.NotNull(session);
        Assert.Equal(link.Id, session.PortalLinkId);
        Assert.Equal(_client.Id, session.ClientId);
        Assert.Equal("127.0.0.1", session.IpAddress);
    }

    [Fact]
    public async Task UpdateSessionActivityAsync_UpdatesLastActivityAt()
    {
        var dto = new CreatePortalLinkDto { ClientId = _client.Id, ExpiryDays = 30 };
        var link = await _service.CreateLinkAsync(dto, "user-1");
        var session = await _service.CreateSessionAsync(link.Id, _client.Id, "127.0.0.1");

        var originalActivity = session.LastActivityAt;
        await Task.Delay(50); // Small delay to ensure time difference
        await _service.UpdateSessionActivityAsync(session.Id);

        var updated = await _db.PortalSessions.FindAsync(session.Id);
        Assert.True(updated!.LastActivityAt >= originalActivity);
    }

    // ========== DASHBOARD ==========

    [Fact]
    public async Task GetDashboardAsync_ReturnsCounts()
    {
        var branding = new PortalBrandingDto { AgencyName = "Test Agency", PrimaryColor = "#000" };
        var dashboard = await _service.GetDashboardAsync(_client.Id, branding);

        Assert.Equal("John Doe", dashboard.ClientName);
        Assert.Equal("Test Agency", dashboard.AgencyName);
        Assert.Equal(1, dashboard.BookingCount);
        Assert.Equal(1, dashboard.QuoteCount);
    }

    // ========== BOOKINGS ==========

    [Fact]
    public async Task GetBookingsAsync_ReturnsClientBookings()
    {
        var bookings = await _service.GetBookingsAsync(_client.Id);

        Assert.Single(bookings.Items);
        Assert.Equal("BK-001", bookings.Items[0].Reference);
        Assert.Equal("Confirmed", bookings.Items[0].Status);
    }

    [Fact]
    public async Task GetBookingsAsync_DoesNotReturnOtherClientBookings()
    {
        var other = new Client { Name = "Other", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(other);
        await _db.SaveChangesAsync();

        var bookings = await _service.GetBookingsAsync(other.Id);
        Assert.Empty(bookings.Items);
    }

    [Fact]
    public async Task GetBookingDetailAsync_ReturnsDetailWithItems()
    {
        var booking = await _db.Bookings.FirstAsync(b => b.ClientId == _client.Id);
        _db.BookingItems.Add(new BookingItem
        {
            BookingId = booking.Id,
            ServiceName = "Hotel Stay",
            ServiceKind = saas.Modules.Inventory.Entities.InventoryItemKind.Hotel,
            ServiceDate = new DateOnly(2025, 7, 1)
        });
        await _db.SaveChangesAsync();

        var detail = await _service.GetBookingDetailAsync(_client.Id, booking.Id);

        Assert.NotNull(detail);
        Assert.Equal("BK-001", detail.Reference);
        Assert.Single(detail.Items);
        Assert.Equal("Hotel Stay", detail.Items[0].Description);
    }

    [Fact]
    public async Task GetBookingDetailAsync_WrongClient_ReturnsNull()
    {
        var booking = await _db.Bookings.FirstAsync(b => b.ClientId == _client.Id);
        var detail = await _service.GetBookingDetailAsync(Guid.NewGuid(), booking.Id);
        Assert.Null(detail);
    }

    // ========== QUOTES ==========

    [Fact]
    public async Task GetQuotesAsync_ReturnsClientQuotes()
    {
        var quotes = await _service.GetQuotesAsync(_client.Id);

        Assert.Single(quotes.Items);
        Assert.Equal("QT-001", quotes.Items[0].Reference);
    }

    // ========== DOCUMENTS ==========

    [Fact]
    public async Task GetDocumentsAsync_ReturnsClientDocuments()
    {
        _db.Documents.Add(new Document
        {
            ClientId = _client.Id,
            FileName = "passport.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            StorageKey = "docs/passport.pdf",
            DocumentType = DocumentType.Passport,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var docs = await _service.GetDocumentsAsync(_client.Id);

        Assert.Single(docs.Items);
        Assert.Equal("passport.pdf", docs.Items[0].FileName);
    }

    [Fact]
    public async Task GetDocumentsAsync_NoDocuments_ReturnsEmpty()
    {
        var docs = await _service.GetDocumentsAsync(_client.Id);
        Assert.Empty(docs.Items);
    }
}
