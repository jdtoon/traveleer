using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using saas.Modules.Billing.DTOs;
using saas.Modules.Billing.Models;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Typed HTTP client wrapping the Paystack REST API.
/// Registered via AddHttpClient&lt;PaystackClient&gt; in BillingModule.
/// </summary>
public class PaystackClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PaystackClient> _logger;

    public PaystackClient(HttpClient httpClient, IOptions<PaystackOptions> options,
        ILogger<PaystackClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.paystack.co/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Plans ──────────────────────────────────────────────────────

    public async Task<PaystackPlanResponse?> CreatePlanAsync(PaystackCreatePlanRequest request)
    {
        _logger.LogInformation("Creating Paystack plan: {Name}", request.Name);
        var response = await _httpClient.PostAsJsonAsync("plan", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PaystackApiResponse<PaystackPlanResponse>>();
        return result?.Data;
    }

    public async Task<List<PaystackPlanResponse>> ListPlansAsync()
    {
        var response = await _httpClient.GetAsync("plan");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PaystackApiListResponse<PaystackPlanResponse>>();
        return result?.Data ?? [];
    }

    // ── Transactions ───────────────────────────────────────────────

    public async Task<PaystackInitializeResponse?> InitializeTransactionAsync(
        PaystackInitializeRequest request)
    {
        _logger.LogInformation("Initializing Paystack transaction for {Email}", request.Email);
        var response = await _httpClient.PostAsJsonAsync("transaction/initialize", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<PaystackApiResponse<PaystackInitializeResponse>>();
        return result?.Data;
    }

    public async Task<PaystackTransactionResponse?> VerifyTransactionAsync(string reference)
    {
        _logger.LogInformation("Verifying Paystack transaction: {Reference}", reference);
        var response = await _httpClient.GetAsync($"transaction/verify/{Uri.EscapeDataString(reference)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<PaystackApiResponse<PaystackTransactionResponse>>();
        return result?.Data;
    }

    // ── Subscriptions ──────────────────────────────────────────────

    public async Task<PaystackSubscriptionResponse?> CreateSubscriptionAsync(
        PaystackCreateSubscriptionRequest request)
    {
        _logger.LogInformation("Creating Paystack subscription for customer: {Customer}", request.Customer);
        var response = await _httpClient.PostAsJsonAsync("subscription", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<PaystackApiResponse<PaystackSubscriptionResponse>>();
        return result?.Data;
    }

    public async Task<bool> DisableSubscriptionAsync(string subscriptionCode, string emailToken)
    {
        _logger.LogInformation("Disabling Paystack subscription: {Code}", subscriptionCode);
        var response = await _httpClient.PostAsJsonAsync("subscription/disable", new
        {
            code = subscriptionCode,
            token = emailToken
        });
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Fetch subscription details including the email_token needed for disable.
    /// </summary>
    public async Task<PaystackSubscriptionDetailResponse?> FetchSubscriptionAsync(string subscriptionCode)
    {
        _logger.LogInformation("Fetching Paystack subscription: {Code}", subscriptionCode);
        var response = await _httpClient.GetAsync($"subscription/{Uri.EscapeDataString(subscriptionCode)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<PaystackApiResponse<PaystackSubscriptionDetailResponse>>();
        return result?.Data;
    }

    // ── Customers ──────────────────────────────────────────────────

    public async Task<PaystackCustomerResponse?> CreateCustomerAsync(
        PaystackCreateCustomerRequest request)
    {
        _logger.LogInformation("Creating Paystack customer: {Email}", request.Email);
        var response = await _httpClient.PostAsJsonAsync("customer", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content
            .ReadFromJsonAsync<PaystackApiResponse<PaystackCustomerResponse>>();
        return result?.Data;
    }
}
