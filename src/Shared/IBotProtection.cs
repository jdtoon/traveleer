namespace saas.Shared;

/// <summary>
/// Bot protection validation (Cloudflare Turnstile). Implementations: MockBotProtection (dev), TurnstileBotProtection (prod).
/// </summary>
public interface IBotProtection
{
    Task<bool> ValidateAsync(string? token);
}
