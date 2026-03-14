using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.DTOs;

public class ActivityEntryDto
{
    public Guid Id { get; set; }
    public ActivityType ActivityType { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string Icon => ActivityType switch
    {
        ActivityType.StatusChange => "🔄",
        ActivityType.ItemAdded => "➕",
        ActivityType.ItemUpdated => "✏️",
        ActivityType.SupplierRequested => "📧",
        ActivityType.VoucherGenerated => "📄",
        ActivityType.PaymentRecorded => "💰",
        ActivityType.CommentAdded => "💬",
        ActivityType.Assigned => "👤",
        ActivityType.Unassigned => "👤",
        _ => "📋"
    };
}

public class ActivityFeedDto
{
    public Guid BookingId { get; set; }
    public List<ActivityEntryDto> Entries { get; set; } = [];
}

public class BookingAssignmentDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
}

public class BookingAssignmentsDto
{
    public Guid BookingId { get; set; }
    public List<BookingAssignmentDto> Assignments { get; set; } = [];
    public List<TeamMemberOption> AvailableMembers { get; set; } = [];
}

public class TeamMemberOption
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class BookingCommentDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class BookingCommentsDto
{
    public Guid BookingId { get; set; }
    public List<BookingCommentDto> Comments { get; set; } = [];
}

public class CreateCommentDto
{
    public string Content { get; set; } = string.Empty;
}
