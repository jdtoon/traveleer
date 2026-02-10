namespace saas.Shared;

/// <summary>
/// Configuration for local development demo seeding.
/// Bound to "DevSeed" section in appsettings.
/// Only used when DevSeed:Enabled is true (typically only in Development).
/// </summary>
public class DevSeedOptions
{
    public const string SectionName = "DevSeed";

    /// <summary>Whether to seed demo data on startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>Slug for the demo tenant.</summary>
    public string TenantSlug { get; set; } = "demo";

    /// <summary>Display name for the demo tenant.</summary>
    public string TenantName { get; set; } = "Demo Workspace";

    /// <summary>Email for the demo admin user.</summary>
    public string AdminEmail { get; set; } = "admin@demo.local";

    /// <summary>Email for the demo member user.</summary>
    public string MemberEmail { get; set; } = "member@demo.local";

    /// <summary>Plan slug to assign to the demo tenant.</summary>
    public string PlanSlug { get; set; } = "starter";
}
