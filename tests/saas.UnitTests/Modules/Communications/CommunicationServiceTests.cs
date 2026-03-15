using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Modules.Communications.DTOs;
using saas.Modules.Communications.Entities;
using saas.Modules.Communications.Services;
using saas.Shared;
using Xunit;

namespace saas.UnitTests.Modules.Communications;

public class CommunicationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly CommunicationService _service;
    private readonly FakeCurrentUser _currentUser;

    public CommunicationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _currentUser = new FakeCurrentUser("user-1", "admin@test.local");
        _service = new CommunicationService(_db, _currentUser);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<AppUser> SeedUserAsync(string id, string email, string displayName)
    {
        var user = new AppUser
        {
            Id = id,
            Email = email,
            UserName = email,
            DisplayName = displayName,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant()
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetByClientAsync_ReturnsEmptyWhenNoEntries()
    {
        var clientId = Guid.NewGuid();
        var result = await _service.GetByClientAsync(clientId);

        Assert.NotNull(result);
        Assert.Equal(clientId, result.ClientId);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task CreateAsync_PersistsEntryWithCorrectFields()
    {
        var clientId = Guid.NewGuid();
        var dto = new CreateCommunicationDto
        {
            ClientId = clientId,
            Channel = CommunicationChannel.Phone,
            Direction = CommunicationDirection.Inbound,
            Subject = "Follow-up call",
            Content = "Discussed itinerary changes.",
            OccurredAt = new DateTime(2025, 3, 10, 14, 0, 0, DateTimeKind.Utc)
        };

        var entry = await _service.CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(clientId, entry.ClientId);
        Assert.Equal(CommunicationChannel.Phone, entry.Channel);
        Assert.Equal(CommunicationDirection.Inbound, entry.Direction);
        Assert.Equal("Follow-up call", entry.Subject);
        Assert.Equal("Discussed itinerary changes.", entry.Content);
        Assert.Equal(new DateTime(2025, 3, 10, 14, 0, 0, DateTimeKind.Utc), entry.OccurredAt);
        Assert.Equal("user-1", entry.LoggedByUserId);
    }

    [Fact]
    public async Task GetByClientAsync_ReturnsEntriesInReverseChronologicalOrder()
    {
        var clientId = Guid.NewGuid();
        await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = clientId,
            Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound,
            Content = "First entry",
            OccurredAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)
        });
        await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = clientId,
            Channel = CommunicationChannel.Phone,
            Direction = CommunicationDirection.Inbound,
            Content = "Second entry",
            OccurredAt = new DateTime(2025, 2, 1, 10, 0, 0, DateTimeKind.Utc)
        });

        var result = await _service.GetByClientAsync(clientId);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("Second entry", result.Entries[0].Content);
        Assert.Equal("First entry", result.Entries[1].Content);
    }

    [Fact]
    public async Task GetByClientAsync_ReturnsPaginatedEntries()
    {
        var clientId = Guid.NewGuid();

        for (var index = 1; index <= 21; index++)
        {
            await _service.CreateAsync(new CreateCommunicationDto
            {
                ClientId = clientId,
                Channel = CommunicationChannel.Email,
                Direction = CommunicationDirection.Outbound,
                Content = $"Entry {index:D2}",
                OccurredAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index)
            });
        }

        var firstPage = await _service.GetByClientAsync(clientId, page: 1, pageSize: 20);
        var secondPage = await _service.GetByClientAsync(clientId, page: 2, pageSize: 20);

        Assert.Equal(20, firstPage.Entries.Count);
        Assert.Single(secondPage.Entries);
        Assert.Equal("Entry 01", secondPage.Entries[0].Content);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(21, firstPage.TotalCount);
    }

    [Fact]
    public async Task GetByBookingAsync_FiltersCorrectly()
    {
        var bookingId = Guid.NewGuid();
        var otherBookingId = Guid.NewGuid();

        await _service.CreateAsync(new CreateCommunicationDto
        {
            BookingId = bookingId, Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound, Content = "Booking entry"
        });
        await _service.CreateAsync(new CreateCommunicationDto
        {
            BookingId = otherBookingId, Channel = CommunicationChannel.Phone,
            Direction = CommunicationDirection.Inbound, Content = "Other booking"
        });

        var result = await _service.GetByBookingAsync(bookingId);

        Assert.Single(result.Entries);
        Assert.Equal("Booking entry", result.Entries[0].Content);
    }

    [Fact]
    public async Task GetBySupplierAsync_FiltersCorrectly()
    {
        var supplierId = Guid.NewGuid();

        await _service.CreateAsync(new CreateCommunicationDto
        {
            SupplierId = supplierId, Channel = CommunicationChannel.WhatsApp,
            Direction = CommunicationDirection.Outbound, Content = "Supplier msg"
        });
        await _service.CreateAsync(new CreateCommunicationDto
        {
            SupplierId = Guid.NewGuid(), Channel = CommunicationChannel.Phone,
            Direction = CommunicationDirection.Inbound, Content = "Other supplier"
        });

        var result = await _service.GetBySupplierAsync(supplierId);

        Assert.Single(result.Entries);
        Assert.Equal("Supplier msg", result.Entries[0].Content);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        var entry = await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = Guid.NewGuid(), Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound, Subject = "Original",
            Content = "Original content"
        });

        await _service.UpdateAsync(entry.Id, new UpdateCommunicationDto
        {
            Channel = CommunicationChannel.Phone,
            Direction = CommunicationDirection.Inbound,
            Subject = "Updated",
            Content = "Updated content",
            OccurredAt = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc)
        });

        var updated = await _db.CommunicationEntries.FindAsync(entry.Id);
        Assert.NotNull(updated);
        Assert.Equal(CommunicationChannel.Phone, updated.Channel);
        Assert.Equal(CommunicationDirection.Inbound, updated.Direction);
        Assert.Equal("Updated", updated.Subject);
        Assert.Equal("Updated content", updated.Content);
        Assert.Equal(new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc), updated.OccurredAt);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        var entry = await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = Guid.NewGuid(), Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound, Content = "To be deleted"
        });

        await _service.DeleteAsync(entry.Id);

        var remaining = await _db.CommunicationEntries.FindAsync(entry.Id);
        Assert.Null(remaining);
    }

    [Fact]
    public async Task AutoLogEmailAsync_CreatesCorrectEntry()
    {
        var supplierId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();

        await _service.AutoLogEmailAsync(null, supplierId, bookingId, "Booking request BK-001", "supplier@example.com");

        var entries = await _db.CommunicationEntries.ToListAsync();
        Assert.Single(entries);
        var entry = entries[0];
        Assert.Null(entry.ClientId);
        Assert.Equal(supplierId, entry.SupplierId);
        Assert.Equal(bookingId, entry.BookingId);
        Assert.Equal(CommunicationChannel.Email, entry.Channel);
        Assert.Equal(CommunicationDirection.Outbound, entry.Direction);
        Assert.Equal("Booking request BK-001", entry.Subject);
        Assert.Contains("supplier@example.com", entry.Content);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForNonexistent()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEntry()
    {
        var entry = await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = Guid.NewGuid(), Channel = CommunicationChannel.InPerson,
            Direction = CommunicationDirection.Inbound, Subject = "Meeting",
            Content = "Met at the office"
        });

        var result = await _service.GetByIdAsync(entry.Id);

        Assert.NotNull(result);
        Assert.Equal(entry.Id, result.Id);
        Assert.Equal("Meeting", result.Subject);
        Assert.Equal("Met at the office", result.Content);
    }

    [Fact]
    public async Task UserNameResolution_ResolvesDisplayName()
    {
        await SeedUserAsync("user-1", "admin@test.local", "Admin User");

        var entry = await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = Guid.NewGuid(), Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound, Content = "Test"
        });

        var clientId = entry.ClientId!.Value;
        var result = await _service.GetByClientAsync(clientId);

        Assert.Single(result.Entries);
        Assert.Equal("Admin User", result.Entries[0].LoggedByName);
    }

    [Fact]
    public async Task UserNameResolution_FallsBackToUserId()
    {
        // No user seeded - should fall back to userId
        var entry = await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = Guid.NewGuid(), Channel = CommunicationChannel.Email,
            Direction = CommunicationDirection.Outbound, Content = "Test"
        });

        var clientId = entry.ClientId!.Value;
        var result = await _service.GetByClientAsync(clientId);

        Assert.Single(result.Entries);
        Assert.Equal("user-1", result.Entries[0].LoggedByName);
    }

    [Fact]
    public async Task CreateAsync_DefaultsOccurredAtToNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var entry = await _service.CreateAsync(new CreateCommunicationDto
        {
            ClientId = Guid.NewGuid(), Channel = CommunicationChannel.Other,
            Direction = CommunicationDirection.Outbound, Content = "No date provided"
        });
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(entry.OccurredAt, before, after);
    }

    [Fact]
    public async Task ChannelIcon_ReturnsCorrectEmoji()
    {
        var dto = new CommunicationEntryDto { Channel = CommunicationChannel.Email, Content = "x" };
        Assert.Equal("✉️", dto.ChannelIcon);

        dto = new CommunicationEntryDto { Channel = CommunicationChannel.Phone, Content = "x" };
        Assert.Equal("📞", dto.ChannelIcon);

        dto = new CommunicationEntryDto { Channel = CommunicationChannel.WhatsApp, Content = "x" };
        Assert.Equal("💬", dto.ChannelIcon);

        dto = new CommunicationEntryDto { Channel = CommunicationChannel.InPerson, Content = "x" };
        Assert.Equal("🤝", dto.ChannelIcon);

        dto = new CommunicationEntryDto { Channel = CommunicationChannel.Other, Content = "x" };
        Assert.Equal("📝", dto.ChannelIcon);
    }

    [Fact]
    public async Task DirectionLabels_ReturnCorrectValues()
    {
        var outbound = new CommunicationEntryDto { Direction = CommunicationDirection.Outbound, Content = "x" };
        Assert.Equal("↗", outbound.DirectionArrow);
        Assert.Equal("Outbound", outbound.DirectionLabel);

        var inbound = new CommunicationEntryDto { Direction = CommunicationDirection.Inbound, Content = "x" };
        Assert.Equal("↙", inbound.DirectionArrow);
        Assert.Equal("Inbound", inbound.DirectionLabel);
    }

    private class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string userId, string email)
        {
            UserId = userId;
            Email = email;
        }
        public string? UserId { get; }
        public string? Email { get; }
        public string? DisplayName => Email;
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool HasAnyPermission(params string[] permissions) => false;
    }
}
