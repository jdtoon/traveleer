using saas.Data;
using saas.Modules.Clients.Entities;

namespace saas.Modules.Portal.Entities;

public enum ClientActionType
{
    AcceptQuote = 0,
    DeclineQuote = 1,
    RequestChange = 2,
    ApproveItinerary = 3,
    SubmitFeedback = 4
}

public enum ClientActionStatus
{
    Pending = 0,
    Acknowledged = 1,
    Actioned = 2,
    Dismissed = 3
}

public class ClientAction : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PortalSessionId { get; set; }
    public PortalSession? PortalSession { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    public ClientActionType ActionType { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? Notes { get; set; }
    public ClientActionStatus Status { get; set; } = ClientActionStatus.Pending;
    public string? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
