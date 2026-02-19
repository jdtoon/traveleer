using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Auth.Filters;
using saas.Modules.TenantAdmin.Models;
using saas.Modules.TenantAdmin.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
[Route("{slug}/admin/settings")]
public class TenantSettingsController : SwapController
{
    private readonly CoreDbContext _coreDb;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantLifecycleService _lifecycle;

    public TenantSettingsController(CoreDbContext coreDb, ITenantContext tenantContext, ITenantLifecycleService lifecycle)
    {
        _coreDb = coreDb;
        _tenantContext = tenantContext;
        _lifecycle = lifecycle;
    }

    [HttpGet("")]
    [HasPermission(TenantAdminPermissions.SettingsRead)]
    public async Task<IActionResult> Index()
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return NotFound();

        return SwapView(SwapViews.TenantSettings.Settings, new TenantSettingsViewModel
        {
            Name = tenant.Name,
            ContactEmail = tenant.ContactEmail,
            Slug = tenant.Slug,
            Status = tenant.Status.ToString(),
            CreatedAt = tenant.CreatedAt,
            PlanName = (await _coreDb.Plans.FindAsync(tenant.PlanId))?.Name ?? "Unknown",
            IsDeleted = tenant.IsDeleted,
            ScheduledDeletionAt = tenant.ScheduledDeletionAt
        });
    }

    [HttpPost("update-general")]
    [HasPermission(TenantAdminPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateGeneral([FromForm] TenantSettingsUpdateModel model)
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ViewData["Error"] = "Organization name is required.";
            return await Index();
        }

        tenant.Name = model.Name.Trim();
        tenant.ContactEmail = model.ContactEmail?.Trim() ?? tenant.ContactEmail;
        await _coreDb.SaveChangesAsync();

        ViewData["Success"] = "Settings updated successfully.";
        return await Index();
    }

    [HttpGet("export-data")]
    [HasPermission(TenantAdminPermissions.SettingsRead)]
    public async Task<IActionResult> ExportData()
    {
        var data = await _lifecycle.ExportTenantDataAsync();
        var slug = _tenantContext.Slug ?? "export";
        return File(data, "application/json", $"{slug}-export-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    [HttpPost("request-deletion")]
    [HasPermission(TenantAdminPermissions.SettingsEdit)]
    public async Task<IActionResult> RequestDeletion()
    {
        await _lifecycle.RequestDeletionAsync(gracePeriodDays: 30);
        ViewData["Success"] = "Deletion requested. Your data will be permanently deleted in 30 days. You can cancel this from the settings page.";
        return await Index();
    }

    [HttpPost("cancel-deletion")]
    [HasPermission(TenantAdminPermissions.SettingsEdit)]
    public async Task<IActionResult> CancelDeletion()
    {
        await _lifecycle.CancelDeletionAsync();
        ViewData["Success"] = "Deletion cancelled. Your organization is safe.";
        return await Index();
    }
}
