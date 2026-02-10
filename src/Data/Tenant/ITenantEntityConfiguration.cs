namespace saas.Data.Tenant;

/// <summary>
/// Marker interface for EF Core entity configurations that belong to the TenantDbContext.
/// Modules place their tenant entity configuration classes in their own Data/ folders
/// and implement this interface so TenantDbContext discovers them via assembly scanning.
/// </summary>
public interface ITenantEntityConfiguration;
