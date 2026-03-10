using saas.Data;
using saas.Modules.Quotes.Entities;

namespace saas.Modules.Email.Entities;

public enum QuoteEmailDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public class QuoteEmailLog : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? CustomMessage { get; set; }
    public QuoteEmailDeliveryStatus Status { get; set; } = QuoteEmailDeliveryStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public string? SentByDisplayName { get; set; }
    public string? SentByEmail { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
