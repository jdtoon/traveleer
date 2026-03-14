using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Audit.DTOs;
using saas.Modules.Audit.Services;
using saas.Modules.Auth.Filters;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Audit.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(AuditFeatures.AuditLog)]
[Route("{slug}/audit")]
public class AuditDashboardController : SwapController
{
    private readonly IAuditDashboardService _auditService;
    private readonly ITenantContext _tenantContext;

    public AuditDashboardController(
        IAuditDashboardService auditService,
        ITenantContext tenantContext)
    {
        _auditService = auditService;
        _tenantContext = tenantContext;
    }

    [HttpGet("")]
    [HasPermission(AuditPermissions.AuditRead)]
    public async Task<IActionResult> Index([FromQuery] string? entity, [FromQuery] string? action, [FromQuery] string? user, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int page = 1)
    {
        ViewData["Title"] = "Audit Log";
        ViewData["Breadcrumb"] = "Audit Log";

        var vm = await _auditService.GetListAsync(
            _tenantContext.Slug!, entity, action, user, from, to, page);

        return SwapView(vm);
    }

    [HttpGet("list")]
    [HasPermission(AuditPermissions.AuditRead)]
    public async Task<IActionResult> List([FromQuery] string? entity, [FromQuery] string? action, [FromQuery] string? user, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int page = 1)
    {
        var vm = await _auditService.GetListAsync(
            _tenantContext.Slug!, entity, action, user, from, to, page);

        return SwapView(SwapViews.AuditDashboard._List, vm);
    }

    [HttpGet("details/{id}")]
    [HasPermission(AuditPermissions.AuditRead)]
    public async Task<IActionResult> Details(long id)
    {
        var detail = await _auditService.GetDetailAsync(_tenantContext.Slug!, id);
        if (detail is null) return NotFound();

        return SwapView(SwapViews.AuditDashboard._DetailModal, detail);
    }
}
