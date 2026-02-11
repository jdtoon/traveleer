namespace saas.Shared;

/// <summary>
/// Abstraction over email delivery. Implementations: ConsoleEmailService (dev), SmtpEmailService (staging), MailerSendEmailService (prod).
/// </summary>
public interface IEmailService
{
    Task SendAsync(EmailMessage message);
    Task SendMagicLinkAsync(string to, string magicLinkUrl);
}

public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null
);
