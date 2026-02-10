using saas.Modules.Notes.Services;
using saas.Shared;

namespace saas.Modules.Notes;

/// <summary>Feature key constant for the Notes module.</summary>
public static class NotesFeatures
{
    public const string Notes = "notes";
}

/// <summary>Permission key constants for the Notes module.</summary>
public static class NotesPermissions
{
    public const string Read = "notes.read";
    public const string Create = "notes.create";
    public const string Edit = "notes.edit";
    public const string Delete = "notes.delete";
}

public class NotesModule : IModule
{
    public string Name => "Notes";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Notes"] = "Notes"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Notes"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(NotesFeatures.Notes, "Notes", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(NotesPermissions.Read, "View Notes", "Notes", 0),
        new(NotesPermissions.Create, "Create Notes", "Notes", 1),
        new(NotesPermissions.Edit, "Edit Notes", "Notes", 2),
        new(NotesPermissions.Delete, "Delete Notes", "Notes", 3),
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", NotesPermissions.Read),
        new("Member", NotesPermissions.Create),
        new("Member", NotesPermissions.Edit),
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotesService, NotesService>();
    }
}
