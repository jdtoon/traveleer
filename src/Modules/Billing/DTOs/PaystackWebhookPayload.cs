using System.Text.Json.Serialization;

namespace saas.Modules.Billing.DTOs;

public class PaystackWebhookEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public PaystackWebhookData Data { get; set; } = new();
}

public class PaystackWebhookData
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("gateway_response")]
    public string? GatewayResponse { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("subscription")]
    public PaystackWebhookSubscription? Subscription { get; set; }

    [JsonPropertyName("customer")]
    public PaystackWebhookCustomer? Customer { get; set; }
}

public class PaystackWebhookSubscription
{
    [JsonPropertyName("subscription_code")]
    public string Code { get; set; } = string.Empty;
}

public class PaystackWebhookCustomer
{
    [JsonPropertyName("customer_code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}
