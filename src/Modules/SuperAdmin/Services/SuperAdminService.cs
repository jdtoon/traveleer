using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Shared;

namespace saas.Modules.SuperAdmin.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly CoreDbContext _coreDb;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBillingService _billingService;

    public SuperAdminService(CoreDbContext coreDb, IServiceProvider serviceProvider, IBillingService billingService)
    {
        _coreDb = coreDb;
        _serviceProvider = serviceProvider;
        _billingService = billingService;
    }

    // ── Dashboard ────────────────────────────────────────────────────────────

    public async Task<SuperAdminDashboardModel> GetDashboardAsync()
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var tenantCount = await _coreDb.Tenants.CountAsync();
        var activeSubscriptions = await _coreDb.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Active);
        var recentRegistrations = await _coreDb.Tenants
            .CountAsync(t => t.CreatedAt >= thirtyDaysAgo);
        var recentTenants = await _coreDb.Tenants
            .Include(t => t.Plan)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => new TenantListItem
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                ContactEmail = t.ContactEmail,
                Status = t.Status,
                PlanName = t.Plan.Name,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return new SuperAdminDashboardModel
        {
            TenantCount = tenantCount,
            ActiveSubscriptions = activeSubscriptions,
            RecentRegistrations = recentRegistrations,
            RecentTenants = recentTenants
        };
    }

    // ── Tenant Management ────────────────────────────────────────────────────

    public async Task<PaginatedList<TenantListItem>> GetTenantsAsync(string? search = null, int page = 1, int pageSize = 20)
    {
        var query = _coreDb.Tenants.Include(t => t.Plan).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Slug.ToLower().Contains(term) ||
                t.ContactEmail.ToLower().Contains(term));
        }

        var projected = query
            .OrderBy(t => t.Name)
            .Select(t => new TenantListItem
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                ContactEmail = t.ContactEmail,
                Status = t.Status,
                PlanName = t.Plan.Name,
                CreatedAt = t.CreatedAt
            });

        return await PaginatedList<TenantListItem>.CreateAsync(projected, page, pageSize);
    }

    public async Task<TenantDetailModel?> GetTenantDetailAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return null;

        var userCount = await GetTenantUserCountAsync(tenant.Slug);

        // Load all features with plan-enabled status and per-tenant overrides
        var allFeatures = await _coreDb.Features
            .OrderBy(f => f.Module).ThenBy(f => f.Name)
            .ToListAsync();

        var planFeatureIds = await _coreDb.PlanFeatures
            .Where(pf => pf.PlanId == tenant.PlanId)
            .Select(pf => pf.FeatureId)
            .ToListAsync();
        var planFeatureSet = planFeatureIds.ToHashSet();

        var overrides = await _coreDb.TenantFeatureOverrides
            .Where(o => o.TenantId == tenant.Id)
            .ToListAsync();
        var overrideMap = overrides.ToDictionary(o => o.FeatureId);

        var featureItems = allFeatures.Select(f =>
        {
            overrideMap.TryGetValue(f.Id, out var ovr);
            return new TenantFeatureItem
            {
                FeatureId = f.Id,
                Name = f.Name,
                Module = f.Module,
                IsGlobal = f.IsGlobal,
                EnabledByPlan = planFeatureSet.Contains(f.Id),
                OverrideEnabled = ovr?.IsEnabled,
                OverrideReason = ovr?.Reason
            };
        }).ToList();

        return new TenantDetailModel
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            ContactEmail = tenant.ContactEmail,
            Status = tenant.Status,
            PlanName = tenant.Plan.Name,
            PlanId = tenant.PlanId,
            CreatedAt = tenant.CreatedAt,
            SubscriptionStatus = tenant.ActiveSubscription?.Status,
            NextBillingDate = tenant.ActiveSubscription?.NextBillingDate,
            UserCount = userCount,
            Features = featureItems
        };
    }

    public async Task<bool> SuspendTenantAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is null) return false;

        tenant.Status = TenantStatus.Suspended;
        await _coreDb.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ActivateTenantAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is null) return false;

        tenant.Status = TenantStatus.Active;
        await _coreDb.SaveChangesAsync();
        return true;
    }

    // ── Plan Management ──────────────────────────────────────────────────────

    public async Task<List<Plan>> GetPlansAsync()
    {
        return await _coreDb.Plans
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    public async Task<Plan?> GetPlanAsync(Guid planId)
    {
        return await _coreDb.Plans.FindAsync(planId);
    }

    public async Task SavePlanAsync(PlanEditModel model)
    {
        Plan plan;
        if (model.Id.HasValue && model.Id.Value != Guid.Empty)
        {
            plan = await _coreDb.Plans.FindAsync(model.Id.Value)
                ?? throw new InvalidOperationException("Plan not found");
        }
        else
        {
            plan = new Plan { Id = Guid.NewGuid() };
            _coreDb.Plans.Add(plan);
        }

        plan.Name = model.Name;
        plan.Slug = model.Slug;
        plan.Description = model.Description;
        plan.MonthlyPrice = model.MonthlyPrice;
        plan.AnnualPrice = model.AnnualPrice;
        plan.MaxUsers = model.MaxUsers;
        plan.SortOrder = model.SortOrder;
        plan.IsActive = model.IsActive;

        await _coreDb.SaveChangesAsync();

        // Push price/name changes to the payment gateway (Paystack)
        if (plan.MonthlyPrice > 0 && !string.IsNullOrEmpty(plan.PaystackPlanCode))
        {
            await _billingService.UpdatePlanInGatewayAsync(plan.Id);
        }
    }

    public async Task<bool> IsSlugTakenAsync(string slug, Guid? excludeId = null)
    {
        var query = _coreDb.Plans.Where(p => p.Slug == slug);
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    // ── Feature Management ───────────────────────────────────────────────────

    public async Task<FeatureMatrixModel> GetFeatureMatrixAsync()
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var features = await _coreDb.Features
            .OrderBy(f => f.Module).ThenBy(f => f.Name)
            .ToListAsync();

        var planFeatures = await _coreDb.PlanFeatures.ToListAsync();

        var enabled = new HashSet<string>(
            planFeatures.Select(pf => $"{pf.PlanId}:{pf.FeatureId}"));

        return new FeatureMatrixModel
        {
            Plans = plans,
            Features = features,
            EnabledCombinations = enabled
        };
    }

    public async Task TogglePlanFeatureAsync(Guid planId, Guid featureId)
    {
        var existing = await _coreDb.PlanFeatures
            .FirstOrDefaultAsync(pf => pf.PlanId == planId && pf.FeatureId == featureId);

        if (existing is not null)
        {
            _coreDb.PlanFeatures.Remove(existing);
        }
        else
        {
            _coreDb.PlanFeatures.Add(new PlanFeature
            {
                PlanId = planId,
                FeatureId = featureId
            });
        }

        await _coreDb.SaveChangesAsync();
    }

    public async Task<TenantFeatureOverrideModel?> GetTenantFeatureOverrideAsync(Guid tenantId, Guid featureId)
    {
        var ovr = await _coreDb.TenantFeatureOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.FeatureId == featureId);

        if (ovr is null) return null;

        return new TenantFeatureOverrideModel
        {
            TenantId = ovr.TenantId,
            FeatureId = ovr.FeatureId,
            IsEnabled = ovr.IsEnabled,
            Reason = ovr.Reason
        };
    }

    public async Task SaveTenantFeatureOverrideAsync(TenantFeatureOverrideModel model)
    {
        var existing = await _coreDb.TenantFeatureOverrides
            .FirstOrDefaultAsync(o => o.TenantId == model.TenantId && o.FeatureId == model.FeatureId);

        if (existing is not null)
        {
            existing.IsEnabled = model.IsEnabled;
            existing.Reason = model.Reason;
        }
        else
        {
            _coreDb.TenantFeatureOverrides.Add(new TenantFeatureOverride
            {
                Id = Guid.NewGuid(),
                TenantId = model.TenantId,
                FeatureId = model.FeatureId,
                IsEnabled = model.IsEnabled,
                Reason = model.Reason
            });
        }

        await _coreDb.SaveChangesAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<int> GetTenantUserCountAsync(string slug)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants", $"{slug}.db");
        if (!File.Exists(dbPath)) return 0;

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var tenantDb = new TenantDbContext(options);
        return await tenantDb.Users.CountAsync();
    }
}
