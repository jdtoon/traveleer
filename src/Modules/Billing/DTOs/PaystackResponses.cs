using System.Text.Json.Serialization;

namespace saas.Modules.Billing.DTOs;

public class PaystackApiResponse<T>
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class PaystackApiListResponse<T>
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = [];
}

public class PaystackInitializeResponse
{
    [JsonPropertyName("authorization_url")]
    public string AuthorizationUrl { get; set; } = string.Empty;

    [JsonPropertyName("access_code")]
    public string AccessCode { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public class PaystackPlanResponse
{
    [JsonPropertyName("plan_code")]
    public string PlanCode { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("interval")]
    public string Interval { get; set; } = string.Empty;
}

public class PaystackTransactionResponse
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("gateway_response")]
    public string? GatewayResponse { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("plan_object")]
    public PaystackPlanResponse? PlanObject { get; set; }

    [JsonPropertyName("customer")]
    public PaystackCustomerResponse? Customer { get; set; }
}

public class PaystackSubscriptionResponse
{
    [JsonPropertyName("subscription_code")]
    public string SubscriptionCode { get; set; } = string.Empty;

    [JsonPropertyName("email_token")]
    public string? EmailToken { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>
/// Extended subscription detail returned by GET /subscription/:id_or_code.
/// Includes email_token needed for disabling a subscription.
/// </summary>
public class PaystackSubscriptionDetailResponse
{
    [JsonPropertyName("subscription_code")]
    public string SubscriptionCode { get; set; } = string.Empty;

    [JsonPropertyName("email_token")]
    public string EmailToken { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("manage_link")]
    public string? ManageLink { get; set; }

    [JsonPropertyName("customer")]
    public PaystackCustomerResponse? Customer { get; set; }
}

public class PaystackCustomerResponse
{
    [JsonPropertyName("customer_code")]
    public string CustomerCode { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}
