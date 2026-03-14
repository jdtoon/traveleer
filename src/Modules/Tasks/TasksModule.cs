using Microsoft.Extensions.DependencyInjection;
using saas.Modules.Tasks.Services;
using saas.Shared;

namespace saas.Modules.Tasks;

public static class TaskFeatures
{
    public const string Tasks = "tasks";
}

public static class TaskPermissions
{
    public const string TasksRead = "tasks.read";
    public const string TasksCreate = "tasks.create";
    public const string TasksEdit = "tasks.edit";
    public const string TasksDelete = "tasks.delete";
}

public class TasksModule : IModule
{
    public string Name => "Tasks";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Task"] = "Tasks"
    };

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(TaskFeatures.Tasks, "Task Management", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(TaskPermissions.TasksRead, "View Tasks", "Tasks", 0),
        new(TaskPermissions.TasksCreate, "Create Tasks", "Tasks", 1),
        new(TaskPermissions.TasksEdit, "Edit Tasks", "Tasks", 2),
        new(TaskPermissions.TasksDelete, "Delete Tasks", "Tasks", 3)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITaskService, TaskService>();
    }
}
