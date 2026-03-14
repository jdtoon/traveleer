using saas.Modules.Portal.Entities;

namespace saas.Modules.Portal.DTOs;

public record ClientActionListItemDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public ClientActionType ActionType { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string? EntityRef { get; init; }
    public string? Notes { get; init; }
    public ClientActionStatus Status { get; init; }
    public string? AcknowledgedByUserId { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public DateTime CreatedAt { get; init; }

    public string ActionLabel => ActionType switch
    {
        ClientActionType.AcceptQuote => "Quote Accepted",
        ClientActionType.DeclineQuote => "Quote Declined",
        ClientActionType.RequestChange => "Change Requested",
        ClientActionType.ApproveItinerary => "Itinerary Approved",
        ClientActionType.SubmitFeedback => "Feedback Submitted",
        _ => ActionType.ToString()
    };

    public string StatusBadge => Status switch
    {
        ClientActionStatus.Pending => "badge-warning",
        ClientActionStatus.Acknowledged => "badge-info",
        ClientActionStatus.Actioned => "badge-success",
        ClientActionStatus.Dismissed => "badge-ghost",
        _ => "badge-ghost"
    };
}

public record ClientActionDetailDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public ClientActionType ActionType { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string? EntityRef { get; init; }
    public string? Notes { get; init; }
    public ClientActionStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record SubmitClientActionDto
{
    public ClientActionType ActionType { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string? Notes { get; init; }
}
