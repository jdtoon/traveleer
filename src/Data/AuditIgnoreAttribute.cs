namespace saas.Data;

/// <summary>
/// Excludes an entity or property from automatic audit trail capture.
/// 
/// On a class: the entire entity is skipped (no audit entries generated).
/// On a property: the property is excluded from OldValues/NewValues serialization.
/// If all changed properties on a Modified entity are [AuditIgnore], the entire entry is skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;
