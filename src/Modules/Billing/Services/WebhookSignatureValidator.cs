using System.Security.Cryptography;
using System.Text;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Verifies Paystack webhook signatures using HMAC-SHA512.
/// </summary>
public static class WebhookSignatureValidator
{
    /// <summary>
    /// Validate that the webhook payload was signed by Paystack using the webhook secret.
    /// </summary>
    public static bool IsValid(string payload, string signature, string webhookSecret)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(webhookSecret))
            return false;

        var hash = HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(webhookSecret),
            Encoding.UTF8.GetBytes(payload)
        );

        var computedSignature = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature)
        );
    }
}
