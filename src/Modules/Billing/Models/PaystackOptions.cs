namespace saas.Modules.Billing.Models;

public class PaystackOptions
{
    public const string SectionName = "Billing:Paystack";

    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string CallbackBaseUrl { get; set; } = string.Empty;
}
