using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Auth.Filters;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.TenantAdmin.Controllers;

[Authorize(Policy = "TenantAdmin")]
public class TenantSettingsController : SwapController
{
    private readonly CoreDbContext _coreDb;
    private readonly ITenantContext _tenantContext;

    public TenantSettingsController(CoreDbContext coreDb, ITenantContext tenantContext)
    {
        _coreDb = coreDb;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(TenantAdminPermissions.SettingsRead)]
    public async Task<IActionResult> Index()
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return NotFound();

        return SwapView("Settings", new TenantSettingsViewModel
        {
            Name = tenant.Name,
            ContactEmail = tenant.ContactEmail,
            CustomDomain = tenant.CustomDomain,
            Slug = tenant.Slug,
            Status = tenant.Status.ToString(),
            CreatedAt = tenant.CreatedAt,
            PlanName = (await _coreDb.Plans.FindAsync(tenant.PlanId))?.Name ?? "Unknown"
        });
    }

    [HttpPost]
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

    [HttpPost]
    [HasPermission(TenantAdminPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateDomain([FromForm] string? customDomain)
    {
        var tenant = await _coreDb.Tenants
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId);

        if (tenant is null) return NotFound();

        tenant.CustomDomain = string.IsNullOrWhiteSpace(customDomain) ? null : customDomain.Trim().ToLowerInvariant();
        await _coreDb.SaveChangesAsync();

        ViewData["Success"] = "Custom domain updated.";
        return await Index();
    }
}

public class TenantSettingsViewModel
{
    public string Name { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? CustomDomain { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string PlanName { get; set; } = string.Empty;
}

public class TenantSettingsUpdateModel
{
    public string Name { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
}
