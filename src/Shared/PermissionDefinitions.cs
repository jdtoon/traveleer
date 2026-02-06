using saas.Data.Tenant;

namespace saas.Shared;

public static class PermissionDefinitions
{
    // Notes Module
    public const string NotesRead = "notes.read";
    public const string NotesCreate = "notes.create";
    public const string NotesEdit = "notes.edit";
    public const string NotesDelete = "notes.delete";

    // User Management
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";

    // Role Management
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";

    // Settings
    public const string SettingsRead = "settings.read";
    public const string SettingsEdit = "settings.edit";

    public static List<Permission> GetAll() =>
    [
        new() { Id = Guid.NewGuid(), Key = NotesRead, Name = "View Notes", Group = "Notes", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = NotesCreate, Name = "Create Notes", Group = "Notes", SortOrder = 1 },
        new() { Id = Guid.NewGuid(), Key = NotesEdit, Name = "Edit Notes", Group = "Notes", SortOrder = 2 },
        new() { Id = Guid.NewGuid(), Key = NotesDelete, Name = "Delete Notes", Group = "Notes", SortOrder = 3 },
        new() { Id = Guid.NewGuid(), Key = UsersRead, Name = "View Users", Group = "Users", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = UsersCreate, Name = "Invite Users", Group = "Users", SortOrder = 1 },
        new() { Id = Guid.NewGuid(), Key = UsersEdit, Name = "Edit Users", Group = "Users", SortOrder = 2 },
        new() { Id = Guid.NewGuid(), Key = UsersDelete, Name = "Deactivate Users", Group = "Users", SortOrder = 3 },
        new() { Id = Guid.NewGuid(), Key = RolesRead, Name = "View Roles", Group = "Roles", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = RolesCreate, Name = "Create Roles", Group = "Roles", SortOrder = 1 },
        new() { Id = Guid.NewGuid(), Key = RolesEdit, Name = "Edit Roles", Group = "Roles", SortOrder = 2 },
        new() { Id = Guid.NewGuid(), Key = RolesDelete, Name = "Delete Roles", Group = "Roles", SortOrder = 3 },
        new() { Id = Guid.NewGuid(), Key = SettingsRead, Name = "View Settings", Group = "Settings", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = SettingsEdit, Name = "Edit Settings", Group = "Settings", SortOrder = 1 },
    ];
}
