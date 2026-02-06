namespace saas.Shared;

/// <summary>
/// Current authenticated user info. Set by CurrentUserMiddleware from cookie claims.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
    bool HasAnyPermission(params string[] permissions);
}
