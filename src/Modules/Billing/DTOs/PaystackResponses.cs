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

    [JsonPropertyName("authorization")]
    public PaystackAuthorizationData? Authorization { get; set; }
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

public class PaystackChargeResponse
{
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("gateway_response")]
    public string? GatewayResponse { get; set; }

    [JsonPropertyName("authorization_url")]
    public string? AuthorizationUrl { get; set; }

    /// <summary>
    /// True if the charge is paused (2FA required). User must be redirected to AuthorizationUrl.
    /// </summary>
    [JsonPropertyName("paused")]
    public bool Paused { get; set; }

    [JsonPropertyName("authorization")]
    public PaystackAuthorizationData? Authorization { get; set; }
}

public class PaystackAuthorizationData
{
    [JsonPropertyName("authorization_code")]
    public string AuthorizationCode { get; set; } = string.Empty;

    [JsonPropertyName("bin")]
    public string? Bin { get; set; }

    [JsonPropertyName("last4")]
    public string? Last4 { get; set; }

    [JsonPropertyName("exp_month")]
    public string? ExpMonth { get; set; }

    [JsonPropertyName("exp_year")]
    public string? ExpYear { get; set; }

    [JsonPropertyName("card_type")]
    public string? CardType { get; set; }

    [JsonPropertyName("bank")]
    public string? Bank { get; set; }

    [JsonPropertyName("reusable")]
    public bool Reusable { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
}

public class PaystackRefundResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("transaction")]
    public PaystackRefundTransactionRef? Transaction { get; set; }
}

public class PaystackRefundTransactionRef
{
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public class PaystackCustomerDetailResponse
{
    [JsonPropertyName("customer_code")]
    public string CustomerCode { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("authorizations")]
    public List<PaystackAuthorizationData>? Authorizations { get; set; }
}

public class PaystackManageLinkResponse
{
    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;
}
