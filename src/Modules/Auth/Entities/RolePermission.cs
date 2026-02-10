namespace saas.Modules.Auth.Entities;

public class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public AppRole Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
