using System.Security.Claims;
using saas.Shared;

namespace saas.Modules.Auth.Services;

public class CurrentUser : ICurrentUser
{
    public string? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; private set; } = Array.Empty<string>();

    public bool HasPermission(string permission)
        => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    public bool HasAnyPermission(params string[] permissions)
        => permissions.Any(p => HasPermission(p));

    public void SetFromClaims(ClaimsPrincipal principal, bool isSuperAdmin)
    {
        IsAuthenticated = principal.Identity?.IsAuthenticated ?? false;
        IsSuperAdmin = isSuperAdmin;
        UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        Email = principal.FindFirstValue(ClaimTypes.Email);
        DisplayName = principal.FindFirstValue(ClaimTypes.Name);

        Roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();
        Permissions = principal.FindAll(AuthClaims.Permission).Select(c => c.Value).Distinct().ToList();
    }

    public void Clear()
    {
        IsAuthenticated = false;
        IsSuperAdmin = false;
        UserId = null;
        Email = null;
        DisplayName = null;
        Roles = Array.Empty<string>();
        Permissions = Array.Empty<string>();
    }
}
