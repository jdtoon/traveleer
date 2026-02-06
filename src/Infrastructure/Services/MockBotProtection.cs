using saas.Shared;

namespace saas.Infrastructure.Services;

public class MockBotProtection : IBotProtection
{
    private readonly ILogger<MockBotProtection> _logger;

    public MockBotProtection(ILogger<MockBotProtection> logger)
    {
        _logger = logger;
    }

    public Task<bool> ValidateAsync(string? token)
    {
        _logger.LogInformation("[MOCK BOT PROTECTION] token={Token}", token ?? "<null>");
        return Task.FromResult(true);
    }
}
