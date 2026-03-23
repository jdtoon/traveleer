using saas.Shared;

namespace saas.Infrastructure.Services;

public class ConsoleEmailService : IEmailService
{
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(IEmailTemplateService templateService, ILogger<ConsoleEmailService> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message)
    {
        _logger.LogInformation("[EMAIL] To={To} Subject={Subject}\n{Body}", message.To, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }

    public Task SendMagicLinkAsync(string to, string magicLinkUrl)
    {
        var htmlBody = _templateService.Render("MagicLink", new Dictionary<string, string>
        {
            ["MagicLinkUrl"] = magicLinkUrl
        });

        _logger.LogInformation("\n================ MAGIC LINK ================\nTo: {To}\nUrl: {Url}\n===========================================\n", to, magicLinkUrl);

        return SendAsync(new EmailMessage(
            To: to,
            Subject: "Your magic link",
            HtmlBody: htmlBody,
            PlainTextBody: $"Sign in using this link: {magicLinkUrl}"));
    }

    public Task SendPasswordResetAsync(string to, string resetUrl)
    {
        var htmlBody = _templateService.Render("PasswordReset", new Dictionary<string, string>
        {
            ["ResetUrl"] = resetUrl
        });

        _logger.LogInformation("\n================ PASSWORD RESET ================\nTo: {To}\nUrl: {Url}\n================================================\n", to, resetUrl);

        return SendAsync(new EmailMessage(
            To: to,
            Subject: "Reset your password",
            HtmlBody: htmlBody,
            PlainTextBody: $"Reset your password using this link: {resetUrl}"));
    }
}
