using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Audit;
using saas.Modules.Auth.Filters;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Audit.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(AuditFeatures.AuditLog)]
public class AuditLogController : SwapController
{
    private readonly AuditDbContext _auditDb;
    private readonly ITenantContext _tenantContext;

    public AuditLogController(AuditDbContext auditDb, ITenantContext tenantContext)
    {
        _auditDb = auditDb;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? entity, string? action, int page = 1)
    {
        ViewData["Breadcrumb"] = "Audit Log";

        var query = _auditDb.AuditEntries
            .Where(a => a.TenantSlug == _tenantContext.Slug)
            .AsNoTracking();

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
                     Timestamp = a.Timestamp,
                     HasChanges = a.OldValues != null || a.NewValues != null
                 }),
            page, 25);

        var vm = new AuditLogViewModel
        {
            Entries = entries,
            FilterEntity = entity,
            FilterAction = action
        };

        return SwapView(vm);
    }

    [HttpGet]
    public async Task<IActionResult> List(string? entity, string? action, int page = 1)
    {
        var query = _auditDb.AuditEntries
            .Where(a => a.TenantSlug == _tenantContext.Slug)
            .AsNoTracking();

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
                     Timestamp = a.Timestamp,
                     HasChanges = a.OldValues != null || a.NewValues != null
                 }),
            page, 25);

        var vm = new AuditLogViewModel
        {
            Entries = entries,
            FilterEntity = entity,
            FilterAction = action
        };

        return SwapView("_AuditLogList", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Detail(long id)
    {
        var entry = await _auditDb.AuditEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantSlug == _tenantContext.Slug);

        if (entry is null) return NotFound();

        return SwapView("_AuditDetailModal", entry);
    }
}

public class AuditLogItem
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool HasChanges { get; set; }
}

public class AuditLogViewModel
{
    public PaginatedList<AuditLogItem> Entries { get; set; } = null!;
    public string? FilterEntity { get; set; }
    public string? FilterAction { get; set; }
}
