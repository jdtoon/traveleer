using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Audit;
using saas.Modules.Audit.DTOs;
using saas.Shared;

namespace saas.Modules.Audit.Services;

public interface IAuditDashboardService
{
    Task<AuditDashboardViewModel> GetListAsync(string tenantSlug, string? entity, string? action, string? user, string? from, string? to, int page);
    Task<AuditDetailDto?> GetDetailAsync(string tenantSlug, long id);
    Task<List<string>> GetDistinctEntityTypesAsync(string tenantSlug);
    Task<List<string>> GetDistinctActionsAsync(string tenantSlug);
    Task<List<string>> GetDistinctUsersAsync(string tenantSlug);
}

public class AuditDashboardService : IAuditDashboardService
{
    private readonly AuditDbContext _auditDb;
    private const int PageSize = 25;

    public AuditDashboardService(AuditDbContext auditDb)
    {
        _auditDb = auditDb;
    }

    public async Task<AuditDashboardViewModel> GetListAsync(
        string tenantSlug, string? entity, string? action, string? user,
        string? from, string? to, int page)
    {
        var query = _auditDb.AuditEntries.AsNoTracking()
            .Where(a => a.TenantSlug == tenantSlug);

        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(a => a.EntityType == entity);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrWhiteSpace(user))
            query = query.Where(a => a.UserEmail == user);

        if (DateTime.TryParse(from, out var fromDate))
            query = query.Where(a => a.Timestamp >= fromDate);

        if (DateTime.TryParse(to, out var toDate))
            query = query.Where(a => a.Timestamp < toDate.AddDays(1));

        var entries = await PaginatedList<AuditDashboardItemDto>.CreateAsync(
            query.OrderByDescending(a => a.Timestamp)
                 .Select(a => new AuditDashboardItemDto
                 {
                     Id = a.Id,
                     EntityType = a.EntityType,
                     EntityId = a.EntityId,
                     Action = a.Action,
                     UserEmail = a.UserEmail ?? "system",
                     Timestamp = a.Timestamp,
                     HasChanges = a.OldValues != null || a.NewValues != null
                 }),
            page, PageSize);

        return new AuditDashboardViewModel
        {
            Entries = entries,
            FilterEntity = entity,
            FilterAction = action,
            FilterUser = user,
            FilterFrom = from,
            FilterTo = to,
            DistinctEntityTypes = await GetDistinctEntityTypesAsync(tenantSlug),
            DistinctActions = await GetDistinctActionsAsync(tenantSlug),
            DistinctUsers = await GetDistinctUsersAsync(tenantSlug)
        };
    }

    public async Task<AuditDetailDto?> GetDetailAsync(string tenantSlug, long id)
    {
        var entry = await _auditDb.AuditEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantSlug == tenantSlug);

        if (entry is null) return null;

        var changes = BuildFieldChanges(entry.OldValues, entry.NewValues);
        var affectedColumns = string.IsNullOrEmpty(entry.AffectedColumns)
            ? []
            : entry.AffectedColumns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return new AuditDetailDto
        {
            Id = entry.Id,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            UserEmail = entry.UserEmail ?? "system",
            Timestamp = entry.Timestamp,
            IpAddress = entry.IpAddress,
            AffectedColumns = affectedColumns,
            Changes = changes
        };
    }

    public async Task<List<string>> GetDistinctEntityTypesAsync(string tenantSlug)
    {
        return await _auditDb.AuditEntries.AsNoTracking()
            .Where(a => a.TenantSlug == tenantSlug)
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctActionsAsync(string tenantSlug)
    {
        return await _auditDb.AuditEntries.AsNoTracking()
            .Where(a => a.TenantSlug == tenantSlug)
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctUsersAsync(string tenantSlug)
    {
        return await _auditDb.AuditEntries.AsNoTracking()
            .Where(a => a.TenantSlug == tenantSlug && a.UserEmail != null)
            .Select(a => a.UserEmail!)
            .Distinct()
            .OrderBy(u => u)
            .ToListAsync();
    }

    private static List<AuditFieldChange> BuildFieldChanges(string? oldValuesJson, string? newValuesJson)
    {
        var changes = new List<AuditFieldChange>();
        var oldDict = ParseJson(oldValuesJson);
        var newDict = ParseJson(newValuesJson);

        var allKeys = oldDict.Keys.Union(newDict.Keys).OrderBy(k => k);

        foreach (var key in allKeys)
        {
            oldDict.TryGetValue(key, out var oldVal);
            newDict.TryGetValue(key, out var newVal);

            changes.Add(new AuditFieldChange
            {
                Field = key,
                OldValue = oldVal,
                NewValue = newVal,
                IsChanged = oldVal != newVal
            });
        }

        return changes;
    }

    private static Dictionary<string, string?> ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : prop.Value.ToString();
            }
            return dict;
        }
        catch
        {
            return new();
        }
    }
}
