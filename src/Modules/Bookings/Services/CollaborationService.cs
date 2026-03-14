using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Services;

public interface ICollaborationService
{
    Task<ActivityFeedDto> GetActivityFeedAsync(Guid bookingId);
    Task LogActivityAsync(Guid bookingId, string userId, string userName, ActivityType type, string summary);
    Task<BookingCommentsDto> GetCommentsAsync(Guid bookingId);
    Task<BookingCommentDto> AddCommentAsync(Guid bookingId, string userId, string userName, string content);
    Task<BookingAssignmentsDto> GetAssignmentsAsync(Guid bookingId);
    Task AssignUserAsync(Guid bookingId, string userId, string? assignedByUserId);
    Task UnassignUserAsync(Guid bookingId, string userId);
}

public class CollaborationService : ICollaborationService
{
    private readonly TenantDbContext _db;

    public CollaborationService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<ActivityFeedDto> GetActivityFeedAsync(Guid bookingId)
    {
        var entries = await _db.ActivityEntries
            .AsNoTracking()
            .Where(x => x.BookingId == bookingId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ActivityEntryDto
            {
                Id = x.Id,
                ActivityType = x.ActivityType,
                UserName = x.UserName,
                Summary = x.Summary,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return new ActivityFeedDto { BookingId = bookingId, Entries = entries };
    }

    public async Task LogActivityAsync(Guid bookingId, string userId, string userName, ActivityType type, string summary)
    {
        var entry = new ActivityEntry
        {
            BookingId = bookingId,
            UserId = userId,
            UserName = userName,
            ActivityType = type,
            Summary = summary,
            CreatedAt = DateTime.UtcNow
        };
        _db.ActivityEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    public async Task<BookingCommentsDto> GetCommentsAsync(Guid bookingId)
    {
        var comments = await _db.BookingComments
            .AsNoTracking()
            .Where(x => x.BookingId == bookingId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new BookingCommentDto
            {
                Id = x.Id,
                UserName = x.UserName,
                Content = x.Content,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        return new BookingCommentsDto { BookingId = bookingId, Comments = comments };
    }

    public async Task<BookingCommentDto> AddCommentAsync(Guid bookingId, string userId, string userName, string content)
    {
        var comment = new BookingComment
        {
            BookingId = bookingId,
            UserId = userId,
            UserName = userName,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
        _db.BookingComments.Add(comment);
        await _db.SaveChangesAsync();

        await LogActivityAsync(bookingId, userId, userName, ActivityType.CommentAdded, $"{userName} added a comment");

        return new BookingCommentDto
        {
            Id = comment.Id,
            UserName = comment.UserName,
            Content = comment.Content,
            CreatedAt = comment.CreatedAt
        };
    }

    public async Task<BookingAssignmentsDto> GetAssignmentsAsync(Guid bookingId)
    {
        var assignments = await _db.BookingAssignments
            .AsNoTracking()
            .Where(x => x.BookingId == bookingId)
            .OrderBy(x => x.AssignedAt)
            .ToListAsync();

        var userIds = assignments.Select(x => x.UserId).ToList();
        var userNames = await ResolveUserNamesAsync(userIds);

        var assignmentDtos = assignments.Select(a => new BookingAssignmentDto
        {
            Id = a.Id,
            UserId = a.UserId,
            UserName = userNames.GetValueOrDefault(a.UserId, a.UserId),
            AssignedAt = a.AssignedAt
        }).ToList();

        var allMembers = await _db.Users
            .AsNoTracking()
            .Select(u => new TeamMemberOption
            {
                UserId = u.Id,
                DisplayName = u.DisplayName ?? u.Email ?? u.Id
            })
            .ToListAsync();

        var assignedUserIds = assignments.Select(x => x.UserId).ToHashSet();
        var available = allMembers.Where(m => !assignedUserIds.Contains(m.UserId)).ToList();

        return new BookingAssignmentsDto
        {
            BookingId = bookingId,
            Assignments = assignmentDtos,
            AvailableMembers = available
        };
    }

    public async Task AssignUserAsync(Guid bookingId, string userId, string? assignedByUserId)
    {
        var exists = await _db.BookingAssignments
            .AnyAsync(x => x.BookingId == bookingId && x.UserId == userId);

        if (exists) return;

        var assignment = new BookingAssignment
        {
            BookingId = bookingId,
            UserId = userId,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = assignedByUserId
        };
        _db.BookingAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        var userName = (await ResolveUserNamesAsync([userId])).GetValueOrDefault(userId, userId);
        var byName = assignedByUserId is not null
            ? (await ResolveUserNamesAsync([assignedByUserId])).GetValueOrDefault(assignedByUserId, assignedByUserId)
            : "System";

        await LogActivityAsync(bookingId, assignedByUserId ?? "system", byName,
            ActivityType.Assigned, $"{byName} assigned {userName}");
    }

    public async Task UnassignUserAsync(Guid bookingId, string userId)
    {
        var assignment = await _db.BookingAssignments
            .FirstOrDefaultAsync(x => x.BookingId == bookingId && x.UserId == userId);

        if (assignment is null) return;

        _db.BookingAssignments.Remove(assignment);
        await _db.SaveChangesAsync();

        var userName = (await ResolveUserNamesAsync([userId])).GetValueOrDefault(userId, userId);
        await LogActivityAsync(bookingId, "system", "System",
            ActivityType.Unassigned, $"{userName} was unassigned");
    }

    private async Task<Dictionary<string, string>> ResolveUserNamesAsync(List<string> userIds)
    {
        if (userIds.Count == 0) return [];

        return await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email ?? u.Id);
    }
}
