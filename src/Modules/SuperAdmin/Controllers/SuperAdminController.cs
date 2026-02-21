using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using saas.Data.Audit;
using saas.Data.Core;
using saas.Infrastructure.Middleware;
using saas.Infrastructure.Services;
using saas.Modules.Audit.Entities;
using saas.Modules.Audit.Models;
using saas.Modules.FeatureFlags.Services;
using saas.Modules.SuperAdmin.Services;
using saas.Data;
using Swap.Htmx;

namespace saas.Modules.SuperAdmin.Controllers;

[Authorize(Policy = "SuperAdmin")]
public class SuperAdminController : SwapController
{
    private readonly ISuperAdminService _service;
    private readonly FeatureCacheInvalidator _cacheInvalidator;
    private readonly IMemoryCache _cache;
    private readonly ISuperAdminAuditService _audit;
    private readonly ITenantInspectionService _inspection;
    private readonly IConfiguration _configuration;
    private readonly CoreDbContext _coreDb;
    private readonly AuditDbContext _auditDb;
    private readonly IAnnouncementService _announcementService;

    public SuperAdminController(
        ISuperAdminService service,
        FeatureCacheInvalidator cacheInvalidator,
        IMemoryCache cache,
        ISuperAdminAuditService audit,
        ITenantInspectionService inspection,
        IConfiguration configuration,
        CoreDbContext coreDb,
        AuditDbContext auditDb,
        IAnnouncementService announcementService)
    {
        _service = service;
        _cacheInvalidator = cacheInvalidator;
        _cache = cache;
        _audit = audit;
        _inspection = inspection;
        _configuration = configuration;
        _coreDb = coreDb;
        _auditDb = auditDb;
        _announcementService = announcementService;
    }

    // ── Dashboard ────────────────────────────────────────────────────────────

    [HttpGet("/super-admin")]
    public async Task<IActionResult> Index()
    {
        var model = await _service.GetDashboardAsync();
        return SwapView(model);
    }

    // ── Tenant Management ────────────────────────────────────────────────────

    [HttpGet("/super-admin/tenants")]
    public async Task<IActionResult> Tenants(string? search, int page = 1)
    {
        var tenants = await _service.GetTenantsAsync(search, page);
        ViewData["Search"] = search;
        return SwapView(tenants);
    }

    [HttpGet("/super-admin/tenants/list")]
    public async Task<IActionResult> TenantList(string? search, int page = 1)
    {
        var tenants = await _service.GetTenantsAsync(search, page);
        ViewData["Search"] = search;
        return SwapView(SwapViews.SuperAdmin._TenantList, tenants);
    }

    [HttpGet("/super-admin/tenants/{id}")]
    public async Task<IActionResult> TenantDetail(Guid id)
    {
        var model = await _service.GetTenantDetailAsync(id);
        if (model is null) return NotFound();
        return SwapView(model);
    }

    [HttpPost("/super-admin/tenants/{id}/suspend")]
    public async Task<IActionResult> SuspendTenant(Guid id)
    {
        var success = await _service.SuspendTenantAsync(id);
        if (!success) return NotFound();

        // Invalidate resolution cache so the suspended status takes effect immediately
        var model = await _service.GetTenantDetailAsync(id);
        if (model is not null)
        {
            TenantResolutionMiddleware.InvalidateCache(_cache, model.Slug);
            await _audit.LogAsync("Suspended", "Tenant", id.ToString(), $"Tenant '{model.Slug}' suspended");
        }

        return SwapResponse()
            .WithView(SwapViews.SuperAdmin.TenantDetail, model)
            .WithWarningToast("Tenant suspended")
            .Build();
    }

    [HttpPost("/super-admin/tenants/{id}/activate")]
    public async Task<IActionResult> ActivateTenant(Guid id)
    {
        var success = await _service.ActivateTenantAsync(id);
        if (!success) return NotFound();

        var model = await _service.GetTenantDetailAsync(id);
        if (model is not null)
        {
            TenantResolutionMiddleware.InvalidateCache(_cache, model.Slug);
            await _audit.LogAsync("Activated", "Tenant", id.ToString(), $"Tenant '{model.Slug}' activated");
        }

        return SwapResponse()
            .WithView(SwapViews.SuperAdmin.TenantDetail, model)
            .WithSuccessToast("Tenant activated")
            .Build();
    }

    // ── Plan Management ──────────────────────────────────────────────────────

    [HttpGet("/super-admin/tenants/{tenantId}/change-plan")]
    public async Task<IActionResult> ChangeTenantPlanModal(Guid tenantId)
    {
        var tenant = await _service.GetTenantDetailAsync(tenantId);
        if (tenant is null) return NotFound();

        var plans = await _service.GetPlansAsync();
        ViewData["Plans"] = plans.Where(p => p.IsActive).ToList();
        return SwapView(SwapViews.SuperAdmin._ChangeTenantPlanModal, tenant);
    }

    [HttpPost("/super-admin/tenants/{tenantId}/change-plan")]
    public async Task<IActionResult> ChangeTenantPlan(Guid tenantId, [FromForm] Guid planId)
    {
        var (success, oldPlan, newPlan) = await _service.ChangeTenantPlanAsync(tenantId, planId);
        if (!success) return NotFound();

        await _audit.LogAsync("PlanChanged", "Tenant", tenantId.ToString(),
            $"Plan changed from '{oldPlan}' to '{newPlan}'");

        // Invalidate feature cache for this tenant since plan features may differ
        _cacheInvalidator.InvalidateTenant(tenantId);

        var model = await _service.GetTenantDetailAsync(tenantId);
        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate("main-content", SwapViews.SuperAdmin.TenantDetail, model)
            .WithSuccessToast($"Plan changed to {newPlan}")
            .Build();
    }

    [HttpGet("/super-admin/plans")]
    public async Task<IActionResult> Plans()
    {
        var plans = await _service.GetPlansAsync();
        return SwapView(plans);
    }

    [HttpGet("/super-admin/plans/edit")]
    public async Task<IActionResult> EditPlan(Guid? id)
    {
        PlanEditModel model;
        if (id.HasValue)
        {
            var plan = await _service.GetPlanAsync(id.Value);
            if (plan is null) return NotFound();
            model = new PlanEditModel
            {
                Id = plan.Id,
                Name = plan.Name,
                Slug = plan.Slug,
                Description = plan.Description,
                MonthlyPrice = plan.MonthlyPrice,
                AnnualPrice = plan.AnnualPrice,
                MaxUsers = plan.MaxUsers,
                MaxRequestsPerMinute = plan.MaxRequestsPerMinute,
                SortOrder = plan.SortOrder,
                IsActive = plan.IsActive
            };
        }
        else
        {
            model = new PlanEditModel();
        }

        return SwapView(SwapViews.SuperAdmin._PlanEditModal, model);
    }

    [HttpPost("/super-admin/plans")]
    public async Task<IActionResult> SavePlan(PlanEditModel model)
    {
        if (!ModelState.IsValid)
            return SwapView(SwapViews.SuperAdmin._PlanEditModal, model);

        // Check for duplicate slug
        var duplicateSlug = await _service.IsSlugTakenAsync(model.Slug, model.Id);
        if (duplicateSlug)
        {
            ModelState.AddModelError("Slug", "A plan with this slug already exists.");
            return SwapView(SwapViews.SuperAdmin._PlanEditModal, model);
        }

        // Fetch old slug before save (in case it was renamed) to invalidate stale cache
        string? oldSlug = null;
        if (model.Id.HasValue)
        {
            var existing = await _service.GetPlanAsync(model.Id.Value);
            if (existing is not null && !string.Equals(existing.Slug, model.Slug, StringComparison.OrdinalIgnoreCase))
                oldSlug = existing.Slug;
        }

        await _service.SavePlanAsync(model);
        await _audit.LogAsync(
            model.Id.HasValue ? "Updated" : "Created", "Plan", model.Slug,
            $"Plan '{model.Name}' ({model.Slug}) — {model.MonthlyPrice:C}/mo");

        // Invalidate cached rate limit for this plan so changes take effect immediately
        _cache.Remove($"plan-rate-limit-{model.Slug}");
        if (oldSlug is not null)
            _cache.Remove($"plan-rate-limit-{oldSlug}");

        var plans = await _service.GetPlansAsync();

        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate(SwapElements.PlanList, SwapViews.SuperAdmin._PlanList, plans)
            .WithSavedToast("Plan")
            .Build();
    }

    // ── Feature Management ───────────────────────────────────────────────────

    [HttpGet("/super-admin/features")]
    public async Task<IActionResult> Features()
    {
        var model = await _service.GetFeatureMatrixAsync();
        return SwapView(model);
    }

    [HttpGet("/super-admin/backups")]
    public async Task<IActionResult> Backups()
    {
        var model = await _service.GetLitestreamStatusAsync();
        var databases = await _service.GetDatabaseReplicationInfoAsync();
        ViewBag.Databases = databases;
        return SwapView(model);
    }

    [HttpPost("/super-admin/backups/sync")]
    public async Task<IActionResult> TriggerSync([FromServices] ILitestreamConfigSync? litestreamSync)
    {
        if (litestreamSync is null)
            return SwapResponse().WithErrorToast("Litestream sync service not available").Build();

        await litestreamSync.SyncConfigAsync();
        await _audit.LogAsync("ManualSync", "Litestream", "config", "Manual config sync triggered");

        var model = await _service.GetLitestreamStatusAsync();
        var databases = await _service.GetDatabaseReplicationInfoAsync();
        ViewBag.Databases = databases;

        return SwapResponse()
            .WithView("Backups", model)
            .WithSuccessToast("Config sync triggered")
            .Build();
    }

    [HttpPost("/super-admin/features/{featureId}/toggle")]
    public async Task<IActionResult> ToggleFeature(Guid featureId, [FromForm] Guid planId)
    {
        await _service.TogglePlanFeatureAsync(planId, featureId);
        _cacheInvalidator.Invalidate();
        await _audit.LogAsync("Toggled", "PlanFeature", $"{planId}:{featureId}",
            $"Feature {featureId} toggled for plan {planId}");

        var model = await _service.GetFeatureMatrixAsync();
        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._FeatureMatrix, model)
            .WithSuccessToast("Feature toggled")
            .Build();
    }

    [HttpGet("/super-admin/features/override")]
    public async Task<IActionResult> FeatureOverrideModal(Guid tenantId, Guid featureId)
    {
        var existing = await _service.GetTenantFeatureOverrideAsync(tenantId, featureId);
        var model = existing ?? new TenantFeatureOverrideModel
        {
            TenantId = tenantId,
            FeatureId = featureId,
            IsEnabled = true
        };
        return SwapView(SwapViews.SuperAdmin._FeatureOverrideModal, model);
    }

    [HttpPost("/super-admin/features/override")]
    public async Task<IActionResult> SaveFeatureOverride(TenantFeatureOverrideModel model)
    {
        await _service.SaveTenantFeatureOverrideAsync(model);
        _cacheInvalidator.InvalidateTenant(model.TenantId);
        await _audit.LogAsync(
            model.IsEnabled ? "OverrideEnabled" : "OverrideDisabled",
            "TenantFeatureOverride", $"{model.TenantId}:{model.FeatureId}",
            $"Override for tenant {model.TenantId}, feature {model.FeatureId}: {(model.IsEnabled ? "enabled" : "disabled")} — {model.Reason}");

        var tenant = await _service.GetTenantDetailAsync(model.TenantId);

        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate(SwapElements.FeatureAccessTable, SwapViews.SuperAdmin._FeatureAccessTable, tenant)
            .WithSuccessToast("Override saved")
            .Build();
    }

    // ── Tenant Database Inspector (Item 13) ──────────────────────────────────

    [HttpGet("/super-admin/tenants/{id}/database")]
    public async Task<IActionResult> TenantDatabase(Guid id)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();

        var dbInfo = await _inspection.GetDatabaseInfoAsync(tenant.Slug);
        var users = await _inspection.GetUsersAsync(tenant.Slug);
        var counts = await _inspection.GetDataCountsAsync(tenant.Slug);
        var sessions = await _inspection.GetActiveSessionsAsync(tenant.Slug);
        var invitations = await _inspection.GetPendingInvitationsAsync(tenant.Slug);

        ViewBag.Tenant = tenant;
        ViewBag.Users = users;
        ViewBag.Counts = counts;
        ViewBag.Sessions = sessions;
        ViewBag.Invitations = invitations;

        return SwapView(dbInfo);
    }

    // ── Query Console ────────────────────────────────────────────────────────

    [HttpGet("/super-admin/tenants/{id}/query")]
    public async Task<IActionResult> QueryConsole(Guid id)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();

        var dbInfo = await _inspection.GetDatabaseInfoAsync(tenant.Slug);
        ViewBag.Tenant = tenant;
        ViewBag.Tables = dbInfo?.Tables ?? [];

        return SwapView(new QueryResult());
    }

    [HttpPost("/super-admin/tenants/{id}/query")]
    public async Task<IActionResult> ExecuteQuery(Guid id, [FromForm] string sql)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();

        var result = await _inspection.ExecuteReadOnlyQueryAsync(tenant.Slug, sql);

        return SwapView("_QueryResult", result);
    }

    // ── Tenant Health (Item 14) ──────────────────────────────────────────────

    [HttpGet("/super-admin/tenant-health")]
    public async Task<IActionResult> TenantHealth()
    {
        var model = await _service.GetTenantHealthOverviewAsync();
        return SwapView(model);
    }

    // ── Extended Tenant Actions (Item 15) ────────────────────────────────────

    [HttpGet("/super-admin/tenants/{id}/extend-trial")]
    public async Task<IActionResult> ExtendTrialModal(Guid id)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();
        return SwapView("_ExtendTrialModal", tenant);
    }

    [HttpPost("/super-admin/tenants/{id}/extend-trial")]
    public async Task<IActionResult> ExtendTrial(Guid id, [FromForm] int days)
    {
        var success = await _service.ExtendTrialAsync(id, days);
        if (!success) return NotFound();

        await _audit.LogAsync("ExtendedTrial", "Tenant", id.ToString(), $"Trial extended by {days} days");

        var model = await _service.GetTenantDetailAsync(id);
        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate("main-content", "TenantDetail", model)
            .WithSuccessToast($"Trial extended by {days} days")
            .Build();
    }

    [HttpGet("/super-admin/tenants/{id}/delete")]
    public async Task<IActionResult> DeleteTenantModal(Guid id)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();
        return SwapView("_DeleteTenantModal", tenant);
    }

    [HttpPost("/super-admin/tenants/{id}/delete")]
    public async Task<IActionResult> DeleteTenant(Guid id, [FromForm] string confirmSlug)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();

        if (!string.Equals(confirmSlug, tenant.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return SwapResponse().WithErrorToast("Slug confirmation does not match").Build();
        }

        await _service.ScheduleTenantDeletionAsync(id);
        TenantResolutionMiddleware.InvalidateCache(_cache, tenant.Slug);
        await _audit.LogAsync("ScheduledDeletion", "Tenant", id.ToString(), $"Tenant '{tenant.Slug}' scheduled for deletion");

        var updatedModel = await _service.GetTenantDetailAsync(id);
        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate("main-content", "TenantDetail", updatedModel)
            .WithWarningToast("Tenant scheduled for deletion")
            .Build();
    }

    // ── Impersonation (Item 16) ──────────────────────────────────────────────

    [HttpPost("/super-admin/tenants/{id}/impersonate")]
    public async Task<IActionResult> ImpersonateTenant(Guid id)
    {
        var tenant = await _service.GetTenantDetailAsync(id);
        if (tenant is null) return NotFound();

        await _audit.LogAsync("Impersonated", "Tenant", id.ToString(),
            $"Super admin began impersonation of tenant '{tenant.Slug}'");

        // Redirect to the tenant with impersonation token
        return SwapResponse()
            .WithRedirect($"/{tenant.Slug}/admin?impersonate=true")
            .WithInfoToast($"Entering {tenant.Name} as admin")
            .Build();
    }

    // ── Billing Dashboard (Item 17) ──────────────────────────────────────────

    [HttpGet("/super-admin/billing")]
    public async Task<IActionResult> Billing()
    {
        var model = await _service.GetBillingDashboardAsync();
        return SwapView(model);
    }

    // ── System Config (Item 18) ──────────────────────────────────────────────

    [HttpGet("/super-admin/config")]
    public IActionResult Config()
    {
        var model = BuildConfigTree(_configuration);
        return SwapView(model);
    }

    // ── Admin Management (Item 19) ───────────────────────────────────────────

    [HttpGet("/super-admin/admins")]
    public async Task<IActionResult> Admins()
    {
        var admins = await _service.GetAdminsAsync();
        return SwapView(admins);
    }

    [HttpGet("/super-admin/admins/invite")]
    public IActionResult InviteAdminModal()
    {
        return SwapView("_InviteAdminModal");
    }

    [HttpPost("/super-admin/admins/invite")]
    public async Task<IActionResult> InviteAdmin([FromForm] string email, [FromForm] string displayName)
    {
        await _service.CreateAdminAsync(email, displayName);
        await _audit.LogAsync("Invited", "SuperAdmin", email, $"Admin '{displayName}' ({email}) invited");

        var admins = await _service.GetAdminsAsync();
        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate("admin-list", "_AdminList", admins)
            .WithSuccessToast($"Admin {email} invited")
            .Build();
    }

    [HttpPost("/super-admin/admins/{id}/toggle")]
    public async Task<IActionResult> ToggleAdmin(Guid id)
    {
        var (success, isActive) = await _service.ToggleAdminStatusAsync(id);
        if (!success) return NotFound();

        await _audit.LogAsync(isActive ? "Activated" : "Deactivated", "SuperAdmin", id.ToString());

        var admins = await _service.GetAdminsAsync();
        return SwapResponse()
            .WithView("_AdminList", admins)
            .WithSuccessToast($"Admin {(isActive ? "activated" : "deactivated")}")
            .Build();
    }

    // ── Active Sessions (Item 20) ────────────────────────────────────────────

    [HttpGet("/super-admin/sessions")]
    public async Task<IActionResult> Sessions()
    {
        var model = await _service.GetAllActiveSessionsAsync();
        return SwapView(model);
    }

    // ── Announcements (Item 21) ──────────────────────────────────────────────

    [HttpGet("/super-admin/announcements")]
    public async Task<IActionResult> Announcements()
    {
        var announcements = await _announcementService.GetAllAnnouncementsAsync();
        return SwapView(announcements);
    }

    [HttpGet("/super-admin/announcements/new")]
    public IActionResult NewAnnouncementModal()
    {
        return SwapView("_AnnouncementModal");
    }

    [HttpPost("/super-admin/announcements/send")]
    public async Task<IActionResult> SendAnnouncement(
        [FromForm] string title, [FromForm] string message,
        [FromForm] string type, [FromForm] string? expiresAt)
    {
        DateTime? expires = DateTime.TryParse(expiresAt, out var d) ? d : null;
        var email = User.Identity?.Name;

        await _service.BroadcastAnnouncementAsync(title, message, type, expires, email);
        await _audit.LogAsync("Broadcast", "Announcement", title, $"Type={type}: {message}");

        var announcements = await _announcementService.GetAllAnnouncementsAsync();
        return SwapResponse()
            .WithView(SwapViews.SuperAdmin._ModalClose)
            .AlsoUpdate("announcement-list", "_AnnouncementList", announcements)
            .WithSuccessToast("Announcement created")
            .Build();
    }

    [HttpPost("/super-admin/announcements/{id}/deactivate")]
    public async Task<IActionResult> DeactivateAnnouncement(Guid id)
    {
        await _announcementService.DeactivateAsync(id);
        await _audit.LogAsync("Deactivated", "Announcement", id.ToString());

        var announcements = await _announcementService.GetAllAnnouncementsAsync();
        return SwapView("_AnnouncementList", announcements);
    }

    // ── Audit Log (dedicated super admin route — bypasses tenant feature flag) ──

    [HttpGet("/super-admin/audit-log")]
    public async Task<IActionResult> AuditLog(
        string? source, string? action, string? entity, string? slug,
        string? search, string? from, string? to, int page = 1)
    {
        var vm = await BuildAuditLogViewModel(source, action, entity, slug, search, from, to, page);
        return SwapView(vm);
    }

    [HttpGet("/super-admin/audit-log/list")]
    public async Task<IActionResult> AuditLogList(
        string? source, string? action, string? entity, string? slug,
        string? search, string? from, string? to, int page = 1)
    {
        var vm = await BuildAuditLogViewModel(source, action, entity, slug, search, from, to, page);
        return PartialView("_AuditLogList", vm);
    }

    [HttpGet("/super-admin/audit-log/detail/{id}")]
    public async Task<IActionResult> AuditLogDetail(long id)
    {
        var entry = await _auditDb.AuditEntries.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (entry is null) return NotFound();
        return PartialView("_AuditDetailModal", entry);
    }

    private async Task<AuditLogViewModel> BuildAuditLogViewModel(
        string? source, string? action, string? entity, string? slug,
        string? search, string? from, string? to, int page)
    {
        var query = _auditDb.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(a => a.Source == source);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(a => a.EntityType.Contains(entity));
        if (!string.IsNullOrWhiteSpace(slug))
            query = query.Where(a => a.TenantSlug == slug);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a =>
                (a.EntityId != null && a.EntityId.Contains(search)) ||
                (a.UserEmail != null && a.UserEmail.Contains(search)) ||
                (a.NewValues != null && a.NewValues.Contains(search)));
        if (DateTime.TryParse(from, out var fromDate))
            query = query.Where(a => a.Timestamp >= fromDate);
        if (DateTime.TryParse(to, out var toDate))
            query = query.Where(a => a.Timestamp <= toDate.AddDays(1));

        var entries = await PaginatedList<AuditLogItem>.CreateAsync(
            query.OrderByDescending(a => a.Timestamp)
                 .Select(a => new AuditLogItem
                 {
                     Id = a.Id,
                     Source = a.Source,
                     EntityType = a.EntityType,
                     EntityId = a.EntityId,
                     Action = a.Action,
                     UserEmail = a.UserEmail ?? "system",
                     TenantSlug = a.TenantSlug ?? string.Empty,
                     Timestamp = a.Timestamp,
                     HasChanges = a.OldValues != null || a.NewValues != null
                 }),
            page, 25);

        // Gather distinct values for filter dropdowns
        var distinctSources = await _auditDb.AuditEntries.Select(a => a.Source).Distinct().ToListAsync();
        var distinctActions = await _auditDb.AuditEntries.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();

        return new AuditLogViewModel
        {
            Entries = entries,
            FilterSource = source,
            FilterAction = action,
            FilterEntity = entity,
            FilterSlug = slug,
            FilterSearch = search,
            FilterFrom = from,
            FilterTo = to,
            DistinctSources = distinctSources,
            DistinctActions = distinctActions
        };
    }

    // ── Export (Item 22) ─────────────────────────────────────────────────────

    [HttpGet("/super-admin/export/tenants")]
    public async Task<IActionResult> ExportTenants()
    {
        var csv = await _service.ExportTenantsCsvAsync();
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "tenants.csv");
    }

    [HttpGet("/super-admin/export/billing")]
    public async Task<IActionResult> ExportBilling()
    {
        var csv = await _service.ExportBillingCsvAsync();
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "billing.csv");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<ConfigSection> BuildConfigTree(IConfiguration config)
    {
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "secret", "key", "token", "connectionstring", "apikey",
            "accesskey", "secretkey", "credentials"
        };

        var sections = new List<ConfigSection>();
        foreach (var section in config.GetChildren())
        {
            sections.Add(BuildSection(section, sensitiveKeys));
        }
        return sections;
    }

    private static ConfigSection BuildSection(IConfigurationSection section, HashSet<string> sensitiveKeys)
    {
        var result = new ConfigSection { Key = section.Key };

        if (section.GetChildren().Any())
        {
            foreach (var child in section.GetChildren())
            {
                result.Children.Add(BuildSection(child, sensitiveKeys));
            }
        }
        else
        {
            var isSensitive = sensitiveKeys.Any(sk => section.Key.Contains(sk, StringComparison.OrdinalIgnoreCase)
                || section.Path.Contains(sk, StringComparison.OrdinalIgnoreCase));
            result.Value = isSensitive ? "••••••••" : section.Value;
        }

        return result;
    }
}

public class ConfigSection
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<ConfigSection> Children { get; set; } = [];
    public bool HasChildren => Children.Count > 0;
}
