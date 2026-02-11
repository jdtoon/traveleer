namespace saas.Shared;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Console";
    public string FromAddress { get; set; } = "no-reply@localhost";
    public string FromName { get; set; } = "SaaS App";
    public SmtpOptions Smtp { get; set; } = new();
    public MailerSendOptions MailerSend { get; set; } = new();
}

public class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
}

public class MailerSendOptions
{
    public string ApiToken { get; set; } = string.Empty;
}
