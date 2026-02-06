using saas.Modules.Notes.Services;

namespace saas.Modules.Notes;

public static class NotesModule
{
    public static IServiceCollection AddNotesModule(this IServiceCollection services)
    {
        services.AddScoped<INotesService, NotesService>();
        return services;
    }
}
