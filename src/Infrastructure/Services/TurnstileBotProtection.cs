using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class TurnstileBotProtection : IBotProtection
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly HttpClient _httpClient;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileBotProtection> _logger;

    public TurnstileBotProtection(
        HttpClient httpClient,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileBotProtection> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            _logger.LogWarning("Turnstile secret key is missing");
            return false;
        }

        try
        {
            var response = await _httpClient.PostAsync(
                VerifyUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = _options.SecretKey,
                    ["response"] = token
                }));

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>();
            return result?.Success == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Turnstile verification failed");
            return false;
        }
    }

    private sealed class TurnstileVerifyResponse
    {
        public bool Success { get; set; }
    }
}
