using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Bookings;

public class CollaborationIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public CollaborationIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // --- Activity Feed ---

    [Fact]
    public async Task ActivityFeedPartial_RendersWithoutLayout()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/activity/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No activity recorded yet.");
    }

    [Fact]
    public async Task ActivityFeedPartial_ShowsEntriesAfterActivity()
    {
        var bookingId = await SeedBookingAsync();
        await SeedActivityEntryAsync(bookingId, ActivityType.StatusChange, "Status changed to Confirmed");

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/activity/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Status changed to Confirmed");
    }

    // --- Comments ---

    [Fact]
    public async Task CommentsPartial_RendersWithoutLayout()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/comments/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No comments yet");
    }

    [Fact]
    public async Task CreateComment_OnValidSubmit_PersistsAndReturnsToast()
    {
        var bookingId = await SeedBookingAsync();

        var commentsResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/comments/{bookingId}");
        commentsResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(commentsResponse, "form", new Dictionary<string, string>
        {
            ["Content"] = "Need to follow up with supplier"
        });

        response.AssertSuccess();
        response.AssertToast("Comment added.");
        response.AssertTrigger("bookings.comments.refresh");

        await using var db = OpenTenantDb();
        var comment = await db.BookingComments.SingleAsync(c => c.BookingId == bookingId);
        Assert.Equal("Need to follow up with supplier", comment.Content);
    }

    [Fact]
    public async Task CreateComment_AlsoCreatesActivityEntry()
    {
        var bookingId = await SeedBookingAsync();

        var commentsResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/comments/{bookingId}");
        commentsResponse.AssertSuccess();

        await _client.SubmitFormAsync(commentsResponse, "form", new Dictionary<string, string>
        {
            ["Content"] = "Activity log test"
        });

        await using var db = OpenTenantDb();
        var activity = await db.ActivityEntries
            .SingleAsync(a => a.BookingId == bookingId && a.ActivityType == ActivityType.CommentAdded);
        Assert.Contains("added a comment", activity.Summary);
    }

    [Fact]
    public async Task CommentsPartial_ShowsExistingComments()
    {
        var bookingId = await SeedBookingAsync();
        await SeedCommentAsync(bookingId, "Great progress on this booking!");

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/comments/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Great progress on this booking!");
    }

    // --- Assignments ---

    [Fact]
    public async Task AssignmentsPartial_RendersWithoutLayout()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/assignments/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No team members assigned yet");
    }

    [Fact]
    public async Task AssignUser_PersistsAssignmentAndReturnsToast()
    {
        var bookingId = await SeedBookingAsync();
        var userId = await GetFirstUserIdAsync();

        var assignResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/assignments/{bookingId}");
        assignResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(assignResponse, "form", new Dictionary<string, string>
        {
            ["userId"] = userId
        });

        response.AssertSuccess();
        response.AssertToast("Team member assigned.");
        response.AssertTrigger("bookings.activity.refresh");

        await using var db = OpenTenantDb();
        var assignment = await db.BookingAssignments.SingleAsync(a => a.BookingId == bookingId);
        Assert.Equal(userId, assignment.UserId);
    }

    [Fact]
    public async Task UnassignUser_RemovesAssignment()
    {
        var (bookingId, userId) = await SeedBookingWithAssignmentAsync();

        var assignResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/assignments/{bookingId}");
        assignResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(
            assignResponse,
            $"form[hx-post='/{TenantSlug}/bookings/unassign/{bookingId}/{userId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertToast("Team member unassigned.");

        await using var db = OpenTenantDb();
        Assert.False(await db.BookingAssignments.AnyAsync(a => a.BookingId == bookingId && a.UserId == userId));
    }

    [Fact]
    public async Task AssignUser_CreatesActivityEntry()
    {
        var bookingId = await SeedBookingAsync();
        var userId = await GetFirstUserIdAsync();

        var assignResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/assignments/{bookingId}");
        assignResponse.AssertSuccess();

        await _client.SubmitFormAsync(assignResponse, "form", new Dictionary<string, string>
        {
            ["userId"] = userId
        });

        await using var db = OpenTenantDb();
        var activity = await db.ActivityEntries
            .SingleAsync(a => a.BookingId == bookingId && a.ActivityType == ActivityType.Assigned);
        Assert.Contains("assigned", activity.Summary);
    }

    // --- Booking Detail Page Integration ---

    [Fact]
    public async Task BookingDetails_ShowsTeamCommentsAndActivitySections()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.GetAsync($"/{TenantSlug}/bookings/details/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("Team");
        await response.AssertContainsAsync("Comments");
        await response.AssertContainsAsync("Activity");
        await response.AssertElementExistsAsync("#booking-assignments");
        await response.AssertElementExistsAsync("#booking-comments");
        await response.AssertElementExistsAsync("#booking-activity");
        await response.AssertContainsAsync("hx-trigger=\"revealed, bookings.assignments.refresh from:body\"");
        await response.AssertContainsAsync("hx-trigger=\"revealed, bookings.comments.refresh from:body\"");
        await response.AssertContainsAsync("hx-trigger=\"revealed, bookings.activity.refresh from:body\"");
        await response.AssertContainsAsync("hx-trigger=\"revealed, comms.refresh from:body\"");
    }

    // --- Auth ---

    [Fact]
    public async Task CollaborationRoutes_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var bookingId = await SeedBookingAsync();

        var response = await publicClient.GetAsync($"/{TenantSlug}/bookings/activity/{bookingId}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // --- Helpers ---

    private async Task<Guid> SeedBookingAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.OrderBy(x => x.Name).Select(x => x.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"BK-C-{Guid.NewGuid():N}"[..13],
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

    private async Task SeedActivityEntryAsync(Guid bookingId, ActivityType type, string summary)
    {
        await using var db = OpenTenantDb();
        db.ActivityEntries.Add(new ActivityEntry
        {
            BookingId = bookingId,
            UserId = "seed-user",
            UserName = "Seed User",
            ActivityType = type,
            Summary = summary,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCommentAsync(Guid bookingId, string content)
    {
        await using var db = OpenTenantDb();
        db.BookingComments.Add(new BookingComment
        {
            BookingId = bookingId,
            UserId = "seed-user",
            UserName = "Seed User",
            Content = content,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> GetFirstUserIdAsync()
    {
        await using var db = OpenTenantDb();
        return await db.Users.OrderBy(u => u.Email).Select(u => u.Id).FirstAsync();
    }

    private async Task<(Guid BookingId, string UserId)> SeedBookingWithAssignmentAsync()
    {
        var bookingId = await SeedBookingAsync();
        var userId = await GetFirstUserIdAsync();

        await using var db = OpenTenantDb();
        db.BookingAssignments.Add(new BookingAssignment
        {
            BookingId = bookingId,
            UserId = userId,
            AssignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (bookingId, userId);
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
