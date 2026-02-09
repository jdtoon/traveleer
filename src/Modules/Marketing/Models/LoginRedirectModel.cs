namespace saas.Modules.Marketing.Models;

public class LoginRedirectModel
{
    public string Slug { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
