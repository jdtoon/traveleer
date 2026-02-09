using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using saas.Shared;

namespace saas.Modules.Billing.Controllers;

/// <summary>
/// Receives webhook callbacks from Paystack. Always returns 200 to acknowledge receipt.
/// Rate-limited by the "webhook" policy.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[EnableRateLimiting("webhook")]
public class PaystackWebhookController : ControllerBase
{
    private readonly IBillingService _billing;
    private readonly ILogger<PaystackWebhookController> _logger;

    public PaystackWebhookController(IBillingService billing,
        ILogger<PaystackWebhookController> logger)
    {
        _billing = billing;
        _logger = logger;
    }

    [HttpPost("paystack")]
    public async Task<IActionResult> HandleWebhook()
    {
        // Read raw body
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        // Get signature header
        var signature = Request.Headers["x-paystack-signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Paystack webhook received without signature header");
            return BadRequest("Missing signature");
        }

        // Process the webhook
        var result = await _billing.ProcessWebhookAsync(payload, signature);

        if (!result.Success)
        {
            _logger.LogWarning("Paystack webhook processing failed: {Error}", result.Error);
        }

        // Always return 200 to acknowledge receipt (Paystack retries on non-200)
        return Ok();
    }
}
