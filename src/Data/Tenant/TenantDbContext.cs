using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using saas.Data.Audit;
using saas.Modules.Notes.Entities;
using saas.Shared;

namespace saas.Data.Tenant;

public class TenantDbContext : IdentityDbContext<AppUser, AppRole, string>
{
    private readonly ITenantContext? _tenantContext;
    private readonly IAuditWriter? _auditWriter;
    private readonly ICurrentUser? _currentUser;

    // Used by provisioning, migrations, design-time factory, and tests
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    // Used at runtime via DI — all scoped deps injected here
    [ActivatorUtilitiesConstructor]
    public TenantDbContext(
        DbContextOptions<TenantDbContext> options,
        ITenantContext tenantContext,
        IAuditWriter? auditWriter = null,
        ICurrentUser? currentUser = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    // RBAC
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Application domain entities
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity tables

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantDbContext).Assembly,
            t => t.Namespace?.Contains("Data.Tenant") == true
        );
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();

        // Snapshot tracked changes BEFORE save (EF clears State after SaveChanges)
        var snapshots = _auditWriter is not null
            ? CaptureChangeSnapshots()
            : null;

        var result = await base.SaveChangesAsync(cancellationToken);

        // Enqueue audit entries AFTER save (entity Ids are now populated for Added entities)
        if (snapshots is { Count: > 0 })
        {
            foreach (var snapshot in snapshots)
            {
                await _auditWriter!.WriteAsync(BuildAuditEntry(snapshot));
            }
        }

        return result;
    }

    // ── Change Tracker Snapshot ────────────────────────────────────────────

    private List<ChangeSnapshot> CaptureChangeSnapshots()
    {
        var snapshots = new List<ChangeSnapshot>();

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
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

    private AuditEntry BuildAuditEntry(ChangeSnapshot snapshot)
    {
        // For Added entities, the Id is now set by EF after SaveChanges
        var entityId = snapshot.Entity switch
        {
            IAuditableEntity auditable => GetEntityId(auditable),
            _ => "unknown"
        };

        return new AuditEntry
        {
            TenantSlug = _tenantContext?.Slug,
            EntityType = snapshot.EntityType,
            EntityId = entityId,
            Action = snapshot.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => snapshot.State.ToString()
            },
            UserId = _currentUser?.UserId,
            UserEmail = _currentUser?.Email,
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

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string GetEntityId(IAuditableEntity entity)
    {
        // Use reflection to find an "Id" property — works for Guid, int, string keys
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

            // Skip if values haven't actually changed
            if (Equals(originalValue, currentValue))
                continue;

            old[prop.Metadata.Name] = originalValue;
            @new[prop.Metadata.Name] = currentValue;
            changed.Add(prop.Metadata.Name);
        }

        return (old, @new, changed);
    }

    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;
        var userEmail = _currentUser?.Email;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
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

    // ── Snapshot Record ───────────────────────────────────────────────────

    private sealed class ChangeSnapshot
    {
        public required IAuditableEntity Entity { get; init; }
        public required string EntityType { get; init; }
        public required EntityState State { get; init; }
        public Dictionary<string, object?>? OldValues { get; set; }
        public Dictionary<string, object?>? NewValues { get; set; }
        public List<string>? AffectedColumns { get; set; }
    }
}
