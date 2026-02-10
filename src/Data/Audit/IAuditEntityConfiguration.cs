namespace saas.Data.Audit;

/// <summary>
/// Marker interface for EF Core entity configurations that belong to the AuditDbContext.
/// The Audit module places its configuration in its own Data/ folder
/// and implements this interface so AuditDbContext discovers it via assembly scanning.
/// </summary>
public interface IAuditEntityConfiguration;
