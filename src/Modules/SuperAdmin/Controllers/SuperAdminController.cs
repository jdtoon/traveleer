using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using saas.Infrastructure.Middleware;
using saas.Modules.FeatureFlags.Services;
using saas.Modules.SuperAdmin.Services;
using Swap.Htmx;

namespace saas.Modules.SuperAdmin.Controllers;

[Authorize(Policy = "SuperAdmin")]
public class SuperAdminController : SwapController
{
    private readonly ISuperAdminService _service;
    private readonly FeatureCacheInvalidator _cacheInvalidator;
    private readonly IMemoryCache _cache;

    public SuperAdminController(ISuperAdminService service, FeatureCacheInvalidator cacheInvalidator, IMemoryCache cache)
    {
        _service = service;
        _cacheInvalidator = cacheInvalidator;
        _cache = cache;
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
        return SwapView("_TenantList", tenants);
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
            TenantResolutionMiddleware.InvalidateCache(_cache, model.Slug);

        return SwapResponse()
            .WithView("TenantDetail", model)
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
            TenantResolutionMiddleware.InvalidateCache(_cache, model.Slug);

        return SwapResponse()
            .WithView("TenantDetail", model)
            .WithSuccessToast("Tenant activated")
            .Build();
    }

    // ── Plan Management ──────────────────────────────────────────────────────

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
                SortOrder = plan.SortOrder,
                IsActive = plan.IsActive
            };
        }
        else
        {
            model = new PlanEditModel();
        }

        return SwapView("_PlanEditModal", model);
    }

    [HttpPost("/super-admin/plans")]
    public async Task<IActionResult> SavePlan(PlanEditModel model)
    {
        if (!ModelState.IsValid)
            return SwapView("_PlanEditModal", model);

        // Check for duplicate slug
        var duplicateSlug = await _service.IsSlugTakenAsync(model.Slug, model.Id);
        if (duplicateSlug)
        {
            ModelState.AddModelError("Slug", "A plan with this slug already exists.");
            return SwapView("_PlanEditModal", model);
        }

        await _service.SavePlanAsync(model);
        var plans = await _service.GetPlansAsync();

        return SwapResponse()
            .WithView("_ModalClose")
            .AlsoUpdate("plan-list", "_PlanList", plans)
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

    [HttpPost("/super-admin/features/{featureId}/toggle")]
    public async Task<IActionResult> ToggleFeature(Guid featureId, [FromForm] Guid planId)
    {
        await _service.TogglePlanFeatureAsync(planId, featureId);
        _cacheInvalidator.Invalidate();

        var model = await _service.GetFeatureMatrixAsync();
        return SwapResponse()
            .WithView("_FeatureMatrix", model)
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
        return SwapView("_FeatureOverrideModal", model);
    }

    [HttpPost("/super-admin/features/override")]
    public async Task<IActionResult> SaveFeatureOverride(TenantFeatureOverrideModel model)
    {
        await _service.SaveTenantFeatureOverrideAsync(model);
        _cacheInvalidator.InvalidateTenant(model.TenantId);

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Override saved")
            .Build();
    }
}
