using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using saas.Modules.Billing.DTOs;
using saas.Modules.Billing.Models;
using saas.Modules.Billing.Services;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class PaystackClientTests
{
    private static PaystackClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new PaystackOptions { SecretKey = "sk_test_xxx" });
        return new PaystackClient(httpClient, options, NullLogger<PaystackClient>.Instance);
    }

    // ── Plans ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlanAsync_SendsPostAndReturnsPlan()
    {
        var expected = new PaystackPlanResponse { PlanCode = "PLN_test", Name = "Starter", Amount = 19900, Interval = "monthly" };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiResponse<PaystackPlanResponse> { Status = true, Data = expected });
        var client = CreateClient(handler);

        var result = await client.CreatePlanAsync(new PaystackCreatePlanRequest
        {
            Name = "Starter", Amount = 19900, Interval = "monthly", Currency = "ZAR"
        });

        Assert.NotNull(result);
        Assert.Equal("PLN_test", result.PlanCode);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/plan", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsPlans()
    {
        var plans = new List<PaystackPlanResponse>
        {
            new() { PlanCode = "PLN_1", Name = "Free" },
            new() { PlanCode = "PLN_2", Name = "Pro" }
        };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiListResponse<PaystackPlanResponse> { Status = true, Data = plans });
        var client = CreateClient(handler);

        var result = await client.ListPlansAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }

    // ── Transactions ───────────────────────────────────────────────

    [Fact]
    public async Task InitializeTransactionAsync_ReturnsAuthorizationUrl()
    {
        var expected = new PaystackInitializeResponse { AuthorizationUrl = "https://paystack.com/pay/abc", Reference = "ref_123" };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiResponse<PaystackInitializeResponse> { Status = true, Data = expected });
        var client = CreateClient(handler);

        var result = await client.InitializeTransactionAsync(new PaystackInitializeRequest
        {
            Email = "test@test.com", Amount = 19900, Currency = "ZAR"
        });

        Assert.NotNull(result);
        Assert.Equal("https://paystack.com/pay/abc", result.AuthorizationUrl);
        Assert.Equal("ref_123", result.Reference);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("transaction/initialize", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task VerifyTransactionAsync_ReturnsTransaction()
    {
        var expected = new PaystackTransactionResponse { Reference = "ref_123", Status = "success", Amount = 19900 };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiResponse<PaystackTransactionResponse> { Status = true, Data = expected });
        var client = CreateClient(handler);

        var result = await client.VerifyTransactionAsync("ref_123");

        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("transaction/verify/ref_123", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    // ── Subscriptions ──────────────────────────────────────────────

    [Fact]
    public async Task CreateSubscriptionAsync_ReturnsSubscription()
    {
        var expected = new PaystackSubscriptionResponse { SubscriptionCode = "SUB_test", Status = "active" };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiResponse<PaystackSubscriptionResponse> { Status = true, Data = expected });
        var client = CreateClient(handler);

        var result = await client.CreateSubscriptionAsync(new PaystackCreateSubscriptionRequest
        {
            Customer = "CUS_test", Plan = "PLN_test"
        });

        Assert.NotNull(result);
        Assert.Equal("SUB_test", result.SubscriptionCode);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task DisableSubscriptionAsync_ReturnsTrueOnSuccess()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new { status = true, message = "Subscription disabled" });
        var client = CreateClient(handler);

        var result = await client.DisableSubscriptionAsync("SUB_test", "token_123");

        Assert.True(result);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("subscription/disable", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DisableSubscriptionAsync_ReturnsFalseOnFailure()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, new { status = false });
        var client = CreateClient(handler);

        var result = await client.DisableSubscriptionAsync("SUB_bad", "token_bad");

        Assert.False(result);
    }

    [Fact]
    public async Task FetchSubscriptionAsync_ReturnsDetail()
    {
        var expected = new PaystackSubscriptionDetailResponse
        {
            SubscriptionCode = "SUB_test", EmailToken = "em_tok", Status = "active"
        };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiResponse<PaystackSubscriptionDetailResponse> { Status = true, Data = expected });
        var client = CreateClient(handler);

        var result = await client.FetchSubscriptionAsync("SUB_test");

        Assert.NotNull(result);
        Assert.Equal("em_tok", result.EmailToken);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("subscription/SUB_test", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_PassesQueryParams()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            new PaystackApiListResponse<PaystackSubscriptionDetailResponse> { Status = true, Data = [] });
        var client = CreateClient(handler);

        await client.ListSubscriptionsAsync(customerCode: "CUS_1", planCode: "PLN_1");

        var uri = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("customer=CUS_1", uri);
        Assert.Contains("plan=PLN_1", uri);
    }

    // ── Customers ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateCustomerAsync_ReturnsCustomer()
    {
        var expected = new PaystackCustomerResponse { CustomerCode = "CUS_test", Email = "test@test.com" };
        var handler = new StubHandler(HttpStatusCode.OK, new PaystackApiResponse<PaystackCustomerResponse> { Status = true, Data = expected });
        var client = CreateClient(handler);

        var result = await client.CreateCustomerAsync(new PaystackCreateCustomerRequest
        {
            Email = "test@test.com", FirstName = "Test"
        });

        Assert.NotNull(result);
        Assert.Equal("CUS_test", result.CustomerCode);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("customer", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    // ── UpdatePlan ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePlanAsync_ReturnsTrueOnSuccess()
    {
        var handler = new StubHandler(HttpStatusCode.OK, new { status = true });
        var client = CreateClient(handler);

        var result = await client.UpdatePlanAsync("PLN_test", new PaystackUpdatePlanRequest
        {
            Name = "Updated", Amount = 29900
        });

        Assert.True(result);
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Contains("plan/PLN_test", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    // ── Auth header ────────────────────────────────────────────────

    [Fact]
    public async Task AllRequests_IncludeBearerAuth()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            new PaystackApiListResponse<PaystackPlanResponse> { Status = true, Data = [] });
        var client = CreateClient(handler);

        await client.ListPlansAsync();

        Assert.NotNull(handler.LastRequest!.Headers.Authorization);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sk_test_xxx", handler.LastRequest.Headers.Authorization.Parameter);
    }

    // ── Error handling ─────────────────────────────────────────────

    [Fact]
    public async Task CreatePlanAsync_ThrowsOnNon2xx()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, new { status = false, message = "Unauthorized" });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CreatePlanAsync(new PaystackCreatePlanRequest { Name = "Bad" }));
    }

    // ── Stub handler ───────────────────────────────────────────────

    private class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object _responseBody;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode statusCode, object responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = JsonContent.Create(_responseBody)
            };
            return Task.FromResult(response);
        }
    }
}
