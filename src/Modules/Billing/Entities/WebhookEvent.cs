namespace saas.Modules.Billing.Entities;

public class WebhookEvent
{
    public Guid Id { get; set; }
    public string PaystackEventType { get; set; } = string.Empty;
    public string PaystackReference { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;

    public WebhookEventStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public enum WebhookEventStatus
{
    Received,
    Processing,
    Processed,
    Failed
}
