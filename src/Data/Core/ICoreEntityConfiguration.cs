namespace saas.Data.Core;

/// <summary>
/// Marker interface for EF Core entity configurations that belong to the CoreDbContext.
/// Modules place their core entity configuration classes in their own Data/ folders
/// and implement this interface so CoreDbContext discovers them via assembly scanning.
/// </summary>
public interface ICoreEntityConfiguration;
