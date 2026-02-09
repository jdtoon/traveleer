namespace saas.Shared;

public class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string Provider { get; set; } = "Mock";
    public string SiteKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}
