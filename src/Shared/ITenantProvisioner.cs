namespace saas.Shared;

/// <summary>
/// Provision new tenant databases. Creates DB, seeds roles/permissions/admin user.
/// </summary>
public interface ITenantProvisioner
{
    Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest request);
}

public record TenantProvisionRequest(
    string TenantName,
    string Slug,
    string AdminEmail,
    Guid PlanId
);

public record TenantProvisionResult(
    bool Success,
    string? Error = null,
    Guid? TenantId = null
);
