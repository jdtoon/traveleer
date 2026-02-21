using saas.Data;
using saas.Data.Core;
using saas.Shared;

namespace saas.Modules.SuperAdmin.Services;

public interface ISuperAdminService
{
    // Dashboard
    Task<SuperAdminDashboardModel> GetDashboardAsync();

    // Tenant management
    Task<PaginatedList<TenantListItem>> GetTenantsAsync(string? search = null, int page = 1, int pageSize = 20);
    Task<TenantDetailModel?> GetTenantDetailAsync(Guid tenantId);
    Task<bool> SuspendTenantAsync(Guid tenantId);
    Task<bool> ActivateTenantAsync(Guid tenantId);
    Task<(bool Success, string? OldPlanName, string? NewPlanName)> ChangeTenantPlanAsync(Guid tenantId, Guid newPlanId);

    // Plan management
    Task<List<Plan>> GetPlansAsync();
    Task<Plan?> GetPlanAsync(Guid planId);
    Task SavePlanAsync(PlanEditModel model);
    Task<bool> IsSlugTakenAsync(string slug, Guid? excludeId = null);

    // Feature management
    Task<FeatureMatrixModel> GetFeatureMatrixAsync();
    Task TogglePlanFeatureAsync(Guid planId, Guid featureId);
    Task<TenantFeatureOverrideModel?> GetTenantFeatureOverrideAsync(Guid tenantId, Guid featureId);
    Task SaveTenantFeatureOverrideAsync(TenantFeatureOverrideModel model);

    // Litestream observability
    Task<LitestreamStatusModel> GetLitestreamStatusAsync();
    Task<List<DatabaseReplicationInfo>> GetDatabaseReplicationInfoAsync();

    // Tenant health (Item 14)
    Task<TenantHealthOverviewModel> GetTenantHealthOverviewAsync();

    // Extended tenant management (Item 15)
    Task<bool> ExtendTrialAsync(Guid tenantId, int days);
    Task ScheduleTenantDeletionAsync(Guid tenantId);

    // Billing dashboard (Item 17)
    Task<BillingDashboardModel> GetBillingDashboardAsync();

    // Admin management (Item 19)
    Task<List<SuperAdminListItem>> GetAdminsAsync();
    Task CreateAdminAsync(string email, string displayName);
    Task<(bool success, bool isActive)> ToggleAdminStatusAsync(Guid adminId);

    // Sessions (Item 20)
    Task<AllSessionsModel> GetAllActiveSessionsAsync();

    // Announcements (Item 21)
    Task<Guid> BroadcastAnnouncementAsync(string title, string message, string type, DateTime? expiresAt, string? createdByEmail);

    // Export (Item 22)
    Task<string> ExportTenantsCsvAsync();
    Task<string> ExportBillingCsvAsync();
}

public class SuperAdminDashboardModel
{
    public int TenantCount { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int RecentRegistrations { get; set; }
    public List<TenantListItem> RecentTenants { get; set; } = [];
}

public class TenantListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TenantDetailModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public DateTime CreatedAt { get; set; }
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public int UserCount { get; set; }
    public List<TenantFeatureItem> Features { get; set; } = [];
}

public class TenantFeatureItem
{
    public Guid FeatureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Module { get; set; }
    public bool IsGlobal { get; set; }
    public bool EnabledByPlan { get; set; }
    public bool? OverrideEnabled { get; set; }
    public string? OverrideReason { get; set; }
}

public class PlanEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxRequestsPerMinute { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FeatureMatrixModel
{
    public List<Plan> Plans { get; set; } = [];
    public List<Feature> Features { get; set; } = [];
    public HashSet<string> EnabledCombinations { get; set; } = []; // "planId:featureId"

    public bool IsEnabled(Guid planId, Guid featureId)
        => EnabledCombinations.Contains($"{planId}:{featureId}");
}

public class TenantFeatureOverrideModel
{
    public Guid TenantId { get; set; }
    public Guid FeatureId { get; set; }
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
}

public class DatabaseReplicationInfo
{
    public string FileName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Core, Audit, Hangfire, Tenant
    public long SizeBytes { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
    public string SizeFormatted => FormatBytes(SizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
        return $"{size:0.##} {sizes[order]}";
    }
}

public class TenantHealthOverviewModel
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int SuspendedTenants { get; set; }
    public int TrialingTenants { get; set; }
    public int TotalUsers { get; set; }
    public int TotalActiveSessions { get; set; }
    public List<TenantHealthItem> Tenants { get; set; } = [];
}

public class TenantHealthItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public int ActiveSessions { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public string DatabaseSizeFormatted => FormatBytes(DatabaseSizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
        return $"{size:0.##} {sizes[order]}";
    }
}

public class BillingDashboardModel
{
    public decimal MonthlyRecurringRevenue { get; set; }
    public int TotalActiveSubscriptions { get; set; }
    public int TrialSubscriptions { get; set; }
    public int PastDueSubscriptions { get; set; }
    public int CancelledThisMonth { get; set; }
    public decimal TotalRevenueAllTime { get; set; }
    public List<PlanBreakdownItem> PlanBreakdown { get; set; } = [];
    public List<RecentPaymentItem> RecentPayments { get; set; } = [];
    public List<RecentInvoiceItem> RecentInvoices { get; set; } = [];
}

public class PlanBreakdownItem
{
    public string PlanName { get; set; } = string.Empty;
    public int SubscriberCount { get; set; }
    public decimal MonthlyPrice { get; set; }
}

public class RecentPaymentItem
{
    public string TenantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Status { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
}

public class RecentInvoiceItem
{
    public string TenantName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Status { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
}

public class SuperAdminListItem
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AllSessionsModel
{
    public int TotalSessions { get; set; }
    public List<TenantSessionSummary> TenantSessions { get; set; } = [];
}

public class TenantSessionSummary
{
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public int ActiveSessions { get; set; }
    public List<TenantSessionInfo> Sessions { get; set; } = [];
}
