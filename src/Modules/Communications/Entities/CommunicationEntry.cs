using saas.Data;

namespace saas.Modules.Communications.Entities;

public enum CommunicationChannel
{
    Email = 0,
    Phone = 1,
    WhatsApp = 2,
    InPerson = 3,
    Other = 4
}

public enum CommunicationDirection
{
    Inbound = 0,
    Outbound = 1
}

public class CommunicationEntry : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ClientId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? BookingId { get; set; }
    public CommunicationChannel Channel { get; set; }
    public CommunicationDirection Direction { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string LoggedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
