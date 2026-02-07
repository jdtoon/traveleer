using saas.Modules.Notes.Services;
using saas.Shared;

namespace saas.Modules.Notes;

public class NotesModule : IModule
{
    public string Name => "Notes";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotesService, NotesService>();
    }
}
