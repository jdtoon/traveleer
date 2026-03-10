namespace saas.Shared;

/// <summary>
/// Abstraction over email delivery. Implementations: ConsoleEmailService (dev), SmtpEmailService (staging), MailerSendEmailService (prod).
/// </summary>
public interface IEmailService
{
    Task<EmailSendResult> SendAsync(EmailMessage message);
    Task<EmailSendResult> SendMagicLinkAsync(string to, string magicLinkUrl);
}

public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    IReadOnlyList<EmailAttachment>? Attachments = null
);

public record EmailAttachment(
    string FileName,
    byte[] Content,
    string ContentType,
    string Disposition = "attachment",
    string? ContentId = null
);

public record EmailSendResult(bool Success, string? ErrorMessage = null, string? ProviderMessageId = null)
{
    public static EmailSendResult Succeeded(string? providerMessageId = null) => new(true, null, providerMessageId);
    public static EmailSendResult Failed(string errorMessage) => new(false, errorMessage);
}
