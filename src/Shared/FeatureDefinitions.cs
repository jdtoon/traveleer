using saas.Data.Core;

namespace saas.Shared;

public static class FeatureDefinitions
{
    // Feature keys as constants for compile-time safety
    public const string Notes = "notes";
    public const string Projects = "projects";
    public const string ApiAccess = "api_access";
    public const string AdvancedReports = "advanced_reports";
    public const string CustomRoles = "custom_roles";
    public const string AuditLog = "audit_log";
    public const string WhiteLabel = "white_label";
    public const string Sso = "sso";
    public const string ExportData = "export_data";

    public static List<Feature> GetAll() =>
    [
        new() { Id = Guid.NewGuid(), Key = Notes, Name = "Notes", Module = "Notes", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = Projects, Name = "Projects", Module = "Projects", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = ApiAccess, Name = "API Access", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = AdvancedReports, Name = "Advanced Reports", Module = "Reports", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = CustomRoles, Name = "Custom Roles", Module = "TenantAdmin", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = AuditLog, Name = "Audit Log", Module = "Audit", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = WhiteLabel, Name = "White Label", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = Sso, Name = "Single Sign-On", Module = "Auth", IsGlobal = false, IsEnabled = true },
        new() { Id = Guid.NewGuid(), Key = ExportData, Name = "Data Export", IsGlobal = false, IsEnabled = true },
    ];
}
