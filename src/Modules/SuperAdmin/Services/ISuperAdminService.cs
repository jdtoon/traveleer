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
