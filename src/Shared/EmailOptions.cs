namespace saas.Shared;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Console";
    public string FromAddress { get; set; } = "no-reply@localhost";
    public SesOptions SES { get; set; } = new();
}

public class SesOptions
{
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
}
