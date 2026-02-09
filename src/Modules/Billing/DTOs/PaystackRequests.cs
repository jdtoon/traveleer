using System.Text.Json.Serialization;

namespace saas.Modules.Billing.DTOs;

public class PaystackInitializeRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; } // In kobo/cents

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "ZAR";

    [JsonPropertyName("callback_url")]
    public string? CallbackUrl { get; set; }

    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

public class PaystackCreatePlanRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("interval")]
    public string Interval { get; set; } = "monthly"; // "monthly", "annually"

    [JsonPropertyName("amount")]
    public int Amount { get; set; } // In kobo/cents

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "ZAR";
}

public class PaystackCreateSubscriptionRequest
{
    [JsonPropertyName("customer")]
    public string Customer { get; set; } = string.Empty;

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = string.Empty;
}

public class PaystackCreateCustomerRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

public class PaystackUpdatePlanRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("interval")]
    public string? Interval { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}
