using saas.Data.Core;
using saas.Data.Tenant;
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

    public IReadOnlyList<Feature> Features =>
    [
        new() { Id = Guid.NewGuid(), Key = NotesFeatures.Notes, Name = "Notes", Module = Name, IsGlobal = false, IsEnabled = true }
    ];

    public IReadOnlyList<Permission> Permissions =>
    [
        new() { Id = Guid.NewGuid(), Key = NotesPermissions.Read, Name = "View Notes", Group = "Notes", SortOrder = 0 },
        new() { Id = Guid.NewGuid(), Key = NotesPermissions.Create, Name = "Create Notes", Group = "Notes", SortOrder = 1 },
        new() { Id = Guid.NewGuid(), Key = NotesPermissions.Edit, Name = "Edit Notes", Group = "Notes", SortOrder = 2 },
        new() { Id = Guid.NewGuid(), Key = NotesPermissions.Delete, Name = "Delete Notes", Group = "Notes", SortOrder = 3 },
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotesService, NotesService>();
    }
}
