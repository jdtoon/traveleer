using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using saas.Modules.Clients.Entities;
using Xunit;

namespace saas.UnitTests.Modules.Bookings;

public class CollaborationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly CollaborationService _service;

    public CollaborationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _service = new CollaborationService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Guid> SeedBookingAsync()
    {
        var client = new Client { Name = "Test Client", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        var booking = new Booking
        {
            BookingRef = $"BK-{Guid.NewGuid():N}"[..12],
            ClientId = client.Id,
            Pax = 2,
            TotalSelling = 5000m,
            SellingCurrencyCode = "USD",
            CostCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<AppUser> SeedUserAsync(string email, string displayName)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid().ToString(),
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

    // --- Activity Feed Tests ---

    [Fact]
    public async Task GetActivityFeedAsync_ReturnsEmptyWhenNoActivity()
    {
        var bookingId = await SeedBookingAsync();

        var result = await _service.GetActivityFeedAsync(bookingId);

        Assert.NotNull(result);
        Assert.Equal(bookingId, result.BookingId);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task LogActivityAsync_PersistsEntry()
    {
        var bookingId = await SeedBookingAsync();

        await _service.LogActivityAsync(bookingId, "user-1", "Alice", ActivityType.StatusChange, "Status changed to Confirmed");

        var entries = await _db.ActivityEntries.Where(x => x.BookingId == bookingId).ToListAsync();
        Assert.Single(entries);
        Assert.Equal("Alice", entries[0].UserName);
        Assert.Equal(ActivityType.StatusChange, entries[0].ActivityType);
        Assert.Equal("Status changed to Confirmed", entries[0].Summary);
    }

    [Fact]
    public async Task GetActivityFeedAsync_ReturnsEntriesInReverseChronologicalOrder()
    {
        var bookingId = await SeedBookingAsync();

        await _service.LogActivityAsync(bookingId, "u1", "Alice", ActivityType.ItemAdded, "First");
        await Task.Delay(10);
        await _service.LogActivityAsync(bookingId, "u2", "Bob", ActivityType.CommentAdded, "Second");

        var result = await _service.GetActivityFeedAsync(bookingId);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("Second", result.Entries[0].Summary);
        Assert.Equal("First", result.Entries[1].Summary);
    }

    [Fact]
    public async Task GetActivityFeedAsync_DoesNotReturnEntriesFromOtherBookings()
    {
        var bookingId1 = await SeedBookingAsync();
        var bookingId2 = await SeedBookingAsync();

        await _service.LogActivityAsync(bookingId1, "u1", "Alice", ActivityType.StatusChange, "Booking 1");
        await _service.LogActivityAsync(bookingId2, "u2", "Bob", ActivityType.StatusChange, "Booking 2");

        var result = await _service.GetActivityFeedAsync(bookingId1);

        Assert.Single(result.Entries);
        Assert.Equal("Booking 1", result.Entries[0].Summary);
    }

    // --- Comments Tests ---

    [Fact]
    public async Task GetCommentsAsync_ReturnsEmptyWhenNoComments()
    {
        var bookingId = await SeedBookingAsync();

        var result = await _service.GetCommentsAsync(bookingId);

        Assert.NotNull(result);
        Assert.Equal(bookingId, result.BookingId);
        Assert.Empty(result.Comments);
    }

    [Fact]
    public async Task AddCommentAsync_PersistsCommentAndReturnsDto()
    {
        var bookingId = await SeedBookingAsync();

        var dto = await _service.AddCommentAsync(bookingId, "user-1", "Alice", "This is a test comment");

        Assert.Equal("Alice", dto.UserName);
        Assert.Equal("This is a test comment", dto.Content);

        var comments = await _db.BookingComments.Where(x => x.BookingId == bookingId).ToListAsync();
        Assert.Single(comments);
    }

    [Fact]
    public async Task AddCommentAsync_CreatesActivityEntry()
    {
        var bookingId = await SeedBookingAsync();

        await _service.AddCommentAsync(bookingId, "user-1", "Alice", "Test comment");

        var activities = await _db.ActivityEntries.Where(x => x.BookingId == bookingId).ToListAsync();
        Assert.Single(activities);
        Assert.Equal(ActivityType.CommentAdded, activities[0].ActivityType);
        Assert.Contains("Alice", activities[0].Summary);
    }

    [Fact]
    public async Task GetCommentsAsync_ReturnsCommentsInChronologicalOrder()
    {
        var bookingId = await SeedBookingAsync();

        await _service.AddCommentAsync(bookingId, "u1", "Alice", "First comment");
        await Task.Delay(10);
        await _service.AddCommentAsync(bookingId, "u2", "Bob", "Second comment");

        var result = await _service.GetCommentsAsync(bookingId);

        Assert.Equal(2, result.Comments.Count);
        Assert.Equal("First comment", result.Comments[0].Content);
        Assert.Equal("Second comment", result.Comments[1].Content);
    }

    [Fact]
    public async Task GetCommentsAsync_DoesNotReturnCommentsFromOtherBookings()
    {
        var bookingId1 = await SeedBookingAsync();
        var bookingId2 = await SeedBookingAsync();

        await _service.AddCommentAsync(bookingId1, "u1", "Alice", "Booking 1 comment");
        await _service.AddCommentAsync(bookingId2, "u2", "Bob", "Booking 2 comment");

        var result = await _service.GetCommentsAsync(bookingId1);

        Assert.Single(result.Comments);
        Assert.Equal("Booking 1 comment", result.Comments[0].Content);
    }

    // --- Assignment Tests ---

    [Fact]
    public async Task GetAssignmentsAsync_ReturnsEmptyWhenNoAssignments()
    {
        var bookingId = await SeedBookingAsync();

        var result = await _service.GetAssignmentsAsync(bookingId);

        Assert.NotNull(result);
        Assert.Equal(bookingId, result.BookingId);
        Assert.Empty(result.Assignments);
    }

    [Fact]
    public async Task AssignUserAsync_PersistsAssignment()
    {
        var bookingId = await SeedBookingAsync();
        var user = await SeedUserAsync("alice@test.local", "Alice");

        await _service.AssignUserAsync(bookingId, user.Id, null);

        var assignments = await _db.BookingAssignments.Where(x => x.BookingId == bookingId).ToListAsync();
        Assert.Single(assignments);
        Assert.Equal(user.Id, assignments[0].UserId);
    }

    [Fact]
    public async Task AssignUserAsync_CreatesActivityEntry()
    {
        var bookingId = await SeedBookingAsync();
        var user = await SeedUserAsync("alice@test.local", "Alice");
        var assigner = await SeedUserAsync("bob@test.local", "Bob");

        await _service.AssignUserAsync(bookingId, user.Id, assigner.Id);

        var activities = await _db.ActivityEntries
            .Where(x => x.BookingId == bookingId && x.ActivityType == ActivityType.Assigned)
            .ToListAsync();
        Assert.Single(activities);
        Assert.Contains("Alice", activities[0].Summary);
    }

    [Fact]
    public async Task AssignUserAsync_DoesNotDuplicateAssignment()
    {
        var bookingId = await SeedBookingAsync();
        var user = await SeedUserAsync("alice@test.local", "Alice");

        await _service.AssignUserAsync(bookingId, user.Id, null);
        await _service.AssignUserAsync(bookingId, user.Id, null);

        var count = await _db.BookingAssignments.CountAsync(x => x.BookingId == bookingId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetAssignmentsAsync_ResolvesUserNames()
    {
        var bookingId = await SeedBookingAsync();
        var user = await SeedUserAsync("alice@test.local", "Alice");

        await _service.AssignUserAsync(bookingId, user.Id, null);

        var result = await _service.GetAssignmentsAsync(bookingId);

        Assert.Single(result.Assignments);
        Assert.Equal("Alice", result.Assignments[0].UserName);
    }

    [Fact]
    public async Task GetAssignmentsAsync_ReturnsAvailableMembers()
    {
        var bookingId = await SeedBookingAsync();
        var alice = await SeedUserAsync("alice@test.local", "Alice");
        var bob = await SeedUserAsync("bob@test.local", "Bob");

        await _service.AssignUserAsync(bookingId, alice.Id, null);

        var result = await _service.GetAssignmentsAsync(bookingId);

        Assert.Single(result.Assignments);
        Assert.Contains(result.AvailableMembers, m => m.DisplayName == "Bob");
        Assert.DoesNotContain(result.AvailableMembers, m => m.DisplayName == "Alice");
    }

    [Fact]
    public async Task UnassignUserAsync_RemovesAssignment()
    {
        var bookingId = await SeedBookingAsync();
        var user = await SeedUserAsync("alice@test.local", "Alice");

        await _service.AssignUserAsync(bookingId, user.Id, null);
        await _service.UnassignUserAsync(bookingId, user.Id);

        var count = await _db.BookingAssignments.CountAsync(x => x.BookingId == bookingId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UnassignUserAsync_CreatesActivityEntry()
    {
        var bookingId = await SeedBookingAsync();
        var user = await SeedUserAsync("alice@test.local", "Alice");

        await _service.AssignUserAsync(bookingId, user.Id, null);
        await _service.UnassignUserAsync(bookingId, user.Id);

        var activities = await _db.ActivityEntries
            .Where(x => x.BookingId == bookingId && x.ActivityType == ActivityType.Unassigned)
            .ToListAsync();
        Assert.Single(activities);
        Assert.Contains("Alice", activities[0].Summary);
    }

    [Fact]
    public async Task UnassignUserAsync_DoesNothingIfNotAssigned()
    {
        var bookingId = await SeedBookingAsync();

        await _service.UnassignUserAsync(bookingId, "nonexistent-user");

        var count = await _db.BookingAssignments.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AssignUserAsync_MultipleUsersCanBeAssigned()
    {
        var bookingId = await SeedBookingAsync();
        var alice = await SeedUserAsync("alice@test.local", "Alice");
        var bob = await SeedUserAsync("bob@test.local", "Bob");

        await _service.AssignUserAsync(bookingId, alice.Id, null);
        await _service.AssignUserAsync(bookingId, bob.Id, null);

        var result = await _service.GetAssignmentsAsync(bookingId);

        Assert.Equal(2, result.Assignments.Count);
    }

    // --- Activity Entry DTO Icon Tests ---

    [Theory]
    [InlineData(ActivityType.StatusChange, "🔄")]
    [InlineData(ActivityType.ItemAdded, "➕")]
    [InlineData(ActivityType.CommentAdded, "💬")]
    [InlineData(ActivityType.Assigned, "👤")]
    [InlineData(ActivityType.PaymentRecorded, "💰")]
    public void ActivityEntryDto_Icon_ReturnsCorrectEmoji(ActivityType type, string expectedIcon)
    {
        var dto = new ActivityEntryDto { ActivityType = type };
        Assert.Equal(expectedIcon, dto.Icon);
    }
}
