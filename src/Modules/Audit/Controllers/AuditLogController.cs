using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Audit;
using saas.Modules.Audit.Models;
using saas.Modules.Auth.Filters;
using Swap.Htmx;

namespace saas.Modules.Audit.Controllers;

[Authorize(Policy = "SuperAdmin")]
[RequireFeature(AuditFeatures.AuditLog)]
[Route("super-admin/audit-log")]
public class AuditLogController : SwapController
{
    private readonly AuditDbContext _auditDb;

    public AuditLogController(AuditDbContext auditDb)
    {
        _auditDb = auditDb;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? slug, string? entity, string? action, int page = 1)
    {
        ViewData["Breadcrumb"] = "Audit Log";

        var query = _auditDb.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(slug))
            query = query.Where(a => a.TenantSlug == slug);

        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(a => a.EntityType.Contains(entity));

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        var entries = await PaginatedList<AuditLogItem>.CreateAsync(
            query.OrderByDescending(a => a.Timestamp)
                 .Select(a => new AuditLogItem
                 {
                     Id = a.Id,
                     EntityType = a.EntityType,
                     EntityId = a.EntityId,
                     Action = a.Action,
                     UserEmail = a.UserEmail ?? "system",
                     TenantSlug = a.TenantSlug,
                     Timestamp = a.Timestamp,
                     HasChanges = a.OldValues != null || a.NewValues != null
                 }),
            page, 25);

        var vm = new AuditLogViewModel
        {
            Entries = entries,
            FilterEntity = entity,
            FilterAction = action,
            FilterSlug = slug
        };

        return SwapView(vm);
    }

    [HttpGet("list")]
    public async Task<IActionResult> List(string? slug, string? entity, string? action, int page = 1)
    {
        var query = _auditDb.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(slug))
            query = query.Where(a => a.TenantSlug == slug);

        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(a => a.EntityType.Contains(entity));

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        var entries = await PaginatedList<AuditLogItem>.CreateAsync(
            query.OrderByDescending(a => a.Timestamp)
                 .Select(a => new AuditLogItem
                 {
                     Id = a.Id,
                     EntityType = a.EntityType,
                     EntityId = a.EntityId,
                     Action = a.Action,
                     UserEmail = a.UserEmail ?? "system",
                     TenantSlug = a.TenantSlug,
                     Timestamp = a.Timestamp,
                     HasChanges = a.OldValues != null || a.NewValues != null
                 }),
            page, 25);

        var vm = new AuditLogViewModel
        {
            Entries = entries,
            FilterEntity = entity,
            FilterAction = action,
            FilterSlug = slug
        };

        return SwapView(SwapViews.AuditLog._AuditLogList, vm);
    }

    [HttpGet("detail/{id}")]
    public async Task<IActionResult> Detail(long id)
    {
        var entry = await _auditDb.AuditEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (entry is null) return NotFound();

        return SwapView(SwapViews.AuditLog._AuditDetailModal, entry);
    }
}
