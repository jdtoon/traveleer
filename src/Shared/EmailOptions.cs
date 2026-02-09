namespace saas.Shared;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Console";
    public string FromAddress { get; set; } = "no-reply@localhost";
    public string? SesRegion { get; set; }
}
