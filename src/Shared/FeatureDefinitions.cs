using saas.Data.Core;

namespace saas.Shared;

/// <summary>
/// Cross-cutting feature definitions that don't belong to any specific module.
/// Module-owned features are defined in each module's IModule.Features property.
/// </summary>
public static class FeatureDefinitions
{
    // Cross-cutting feature keys (not owned by any module yet)
    public const string Projects = "projects";
    public const string ApiAccess = "api_access";
    public const string AdvancedReports = "advanced_reports";
    public const string CustomRoles = "custom_roles";
    public const string WhiteLabel = "white_label";
    public const string Sso = "sso";
    public const string ExportData = "export_data";

    /// <summary>
    /// Returns only the cross-cutting features not owned by any module.
    /// Combined with IModule.Features at startup for seeding.
    /// </summary>
    public static List<Feature> GetAll() =>
    [
        new() { Id = Guid.NewGuid(), Key = Projects, Name = "Projects", Module = "Projects", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = ApiAccess, Name = "API Access", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = AdvancedReports, Name = "Advanced Reports", Module = "Reports", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = CustomRoles, Name = "Custom Roles", Module = "TenantAdmin", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = WhiteLabel, Name = "White Label", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = Sso, Name = "Single Sign-On", Module = "Auth", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = ExportData, Name = "Data Export", IsGlobal = false, IsEnabled = true },
    ];
}
