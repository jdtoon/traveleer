using saas.Shared;

namespace saas.Infrastructure.Services;

public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message)
    {
        _logger.LogInformation("[EMAIL] To={To} Subject={Subject}\n{Body}", message.To, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }

    public Task SendMagicLinkAsync(string to, string magicLinkUrl)
    {
        _logger.LogInformation("★ MAGIC LINK for {To}: {Url}", to, magicLinkUrl);
        return Task.CompletedTask;
    }
}
