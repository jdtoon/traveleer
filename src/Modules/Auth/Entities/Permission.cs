namespace saas.Modules.Auth.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
