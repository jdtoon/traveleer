namespace saas.Shared;

/// <summary>
/// Global site settings bound from appsettings.json "Site" section.
/// </summary>
public class SiteSettings
{
    public const string SectionName = "Site";

    /// <summary>
    /// Public base URL of the application (e.g. "https://app.mysite.co.za").
    /// Used for generating absolute URLs in emails and views.
    /// </summary>
    public string BaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>
    /// Display name of the application shown in emails and UI.
    /// </summary>
    public string Name { get; set; } = "SaaS App";
}
