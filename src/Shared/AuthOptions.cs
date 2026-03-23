namespace saas.Shared;

/// <summary>
/// Controls the login method for both tenant users and super admins.
/// Bound to the "Auth" configuration section.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// The login method to use. Valid values: "Password" (default) or "MagicLink".
    /// </summary>
    public string LoginMethod { get; set; } = "Password";

    /// <summary>Returns true when password-based login is active.</summary>
    public bool IsPasswordLogin => string.Equals(LoginMethod, "Password", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when magic-link login is active.</summary>
    public bool IsMagicLinkLogin => string.Equals(LoginMethod, "MagicLink", StringComparison.OrdinalIgnoreCase);
}
