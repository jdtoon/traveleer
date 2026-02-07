using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using saas.Data;
using saas.Data.Audit;
using saas.Shared;

namespace saas.Modules.Audit.Services;

/// <summary>
/// EF Core interceptor that automatically captures entity changes on TenantDbContext
/// and enqueues audit trail entries via <see cref="ChannelAuditWriter"/>.
///
/// This interceptor:
/// 1. Hooks into SavingChanges — snapshots tracked changes BEFORE save
/// 2. Hooks into SavedChanges — builds audit entries AFTER save (IDs are populated for Added entities)
/// 3. Populates CreatedBy/UpdatedBy/CreatedAt/UpdatedAt on IAuditableEntity
/// 4. Respects [AuditIgnore] on classes and properties
/// 5. Never blocks or throws — audit failures are logged and swallowed
///
/// Scoped services (ITenantContext, ICurrentUser) are resolved at call time via
/// IServiceProvider to avoid singleton-scoped lifetime mismatches.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ChannelAuditWriter _auditWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    // AsyncLocal storage for snapshots captured in SavingChanges → consumed in SavedChanges
    private static readonly AsyncLocal<List<ChangeSnapshot>?> _pendingSnapshots = new();

    public AuditSaveChangesInterceptor(
        ChannelAuditWriter auditWriter,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditSaveChangesInterceptor> logger)
    {
        _auditWriter = auditWriter;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // ── SavingChanges (BEFORE save) ────────────────────────────────────────

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            BeforeSave(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            BeforeSave(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // ── SavedChanges (AFTER save) ──────────────────────────────────────────

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSave();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        AfterSave();
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    // ── SaveChangesFailed (cleanup on error) ───────────────────────────────

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pendingSnapshots.Value = null;
        base.SaveChangesFailed(eventData);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pendingSnapshots.Value = null;
        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    // ── Core Logic ─────────────────────────────────────────────────────────

    private void BeforeSave(DbContext context)
    {
        try
        {
            ApplyAuditFields(context);
            _pendingSnapshots.Value = CaptureChangeSnapshots(context);
        }
        catch (Exception ex)
        {
            // Never let audit break the main operation
            _logger.LogWarning(ex, "Failed to capture audit snapshots during SaveChanges");
            _pendingSnapshots.Value = null;
        }
    }

    private void AfterSave()
    {
        var snapshots = _pendingSnapshots.Value;
        _pendingSnapshots.Value = null;

        if (snapshots is not { Count: > 0 })
            return;

        try
        {
            var (tenantSlug, userId, userEmail) = ResolveContext();

            foreach (var snapshot in snapshots)
            {
                var entry = BuildAuditEntry(snapshot, tenantSlug, userId, userEmail);
                _auditWriter.Enqueue(entry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue audit entries after SaveChanges");
        }
    }

    // ── Audit Field Stamping ───────────────────────────────────────────────

    private void ApplyAuditFields(DbContext context)
    {
        var now = DateTime.UtcNow;
        var (_, _, userEmail) = ResolveContext();

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userEmail;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userEmail;
                    break;
            }
        }
    }

    // ── Change Tracker Snapshot ────────────────────────────────────────────

    private static List<ChangeSnapshot> CaptureChangeSnapshots(DbContext context)
    {
        var snapshots = new List<ChangeSnapshot>();

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var entityType = entry.Entity.GetType();

            // Class-level [AuditIgnore] — skip entire entity
            if (entityType.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
                continue;

            // Skip ASP.NET Identity framework entities — they're noise
            if (entityType.Namespace?.StartsWith("Microsoft.AspNetCore.Identity") == true)
                continue;

            var ignoredProps = GetIgnoredProperties(entityType);

            var snapshot = new ChangeSnapshot
            {
                Entity = entry.Entity,
                EntityType = entityType.Name,
                State = entry.State
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    snapshot.NewValues = GetValues(entry, ValueSource.Current, ignoredProps);
                    break;

                case EntityState.Modified:
                    var (oldVals, newVals, changed) = GetChangedValues(entry, ignoredProps);
                    if (changed.Count == 0)
                        continue; // All changed props were [AuditIgnore] — skip
                    snapshot.OldValues = oldVals;
                    snapshot.NewValues = newVals;
                    snapshot.AffectedColumns = changed;
                    break;

                case EntityState.Deleted:
                    snapshot.OldValues = GetValues(entry, ValueSource.Original, ignoredProps);
                    break;
            }

            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private static AuditEntry BuildAuditEntry(
        ChangeSnapshot snapshot, string? tenantSlug, string? userId, string? userEmail)
    {
        var entityId = GetEntityId(snapshot.Entity);

        return new AuditEntry
        {
            TenantSlug = tenantSlug,
            EntityType = snapshot.EntityType,
            EntityId = entityId,
            Action = snapshot.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => snapshot.State.ToString()
            },
            UserId = userId,
            UserEmail = userEmail,
            OldValues = snapshot.OldValues is { Count: > 0 }
                ? JsonSerializer.Serialize(snapshot.OldValues)
                : null,
            NewValues = snapshot.NewValues is { Count: > 0 }
                ? JsonSerializer.Serialize(snapshot.NewValues)
                : null,
            AffectedColumns = snapshot.AffectedColumns is { Count: > 0 }
                ? string.Join(",", snapshot.AffectedColumns)
                : null,
            Timestamp = DateTime.UtcNow
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private (string? TenantSlug, string? UserId, string? UserEmail) ResolveContext()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return (null, null, null);

        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();

        return (tenantContext?.Slug, currentUser?.UserId, currentUser?.Email);
    }

    private static string GetEntityId(IAuditableEntity entity)
    {
        var idProp = entity.GetType().GetProperty("Id");
        return idProp?.GetValue(entity)?.ToString() ?? "unknown";
    }

    private static HashSet<string> GetIgnoredProperties(Type entityType)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in entityType.GetProperties())
        {
            if (prop.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
                ignored.Add(prop.Name);
        }

        // Always exclude the IAuditableEntity bookkeeping fields from values
        ignored.Add(nameof(IAuditableEntity.CreatedAt));
        ignored.Add(nameof(IAuditableEntity.CreatedBy));
        ignored.Add(nameof(IAuditableEntity.UpdatedAt));
        ignored.Add(nameof(IAuditableEntity.UpdatedBy));

        return ignored;
    }

    private enum ValueSource { Current, Original }

    private static Dictionary<string, object?> GetValues(
        EntityEntry entry, ValueSource source, HashSet<string> ignored)
    {
        var values = new Dictionary<string, object?>();
        var propertyValues = source == ValueSource.Current
            ? entry.CurrentValues
            : entry.OriginalValues;

        foreach (var prop in propertyValues.Properties)
        {
            if (ignored.Contains(prop.Name))
                continue;
            values[prop.Name] = propertyValues[prop];
        }

        return values;
    }

    private static (Dictionary<string, object?> old, Dictionary<string, object?> @new, List<string> changed)
        GetChangedValues(EntityEntry entry, HashSet<string> ignored)
    {
        var old = new Dictionary<string, object?>();
        var @new = new Dictionary<string, object?>();
        var changed = new List<string>();

        foreach (var prop in entry.Properties)
        {
            if (ignored.Contains(prop.Metadata.Name))
                continue;

            if (!prop.IsModified)
                continue;

            var originalValue = prop.OriginalValue;
            var currentValue = prop.CurrentValue;

            if (Equals(originalValue, currentValue))
                continue;

            old[prop.Metadata.Name] = originalValue;
            @new[prop.Metadata.Name] = currentValue;
            changed.Add(prop.Metadata.Name);
        }

        return (old, @new, changed);
    }

    // ── Snapshot Record ───────────────────────────────────────────────────

    internal sealed class ChangeSnapshot
    {
        public required IAuditableEntity Entity { get; init; }
        public required string EntityType { get; init; }
        public required EntityState State { get; init; }
        public Dictionary<string, object?>? OldValues { get; set; }
        public Dictionary<string, object?>? NewValues { get; set; }
        public List<string>? AffectedColumns { get; set; }
    }
}
