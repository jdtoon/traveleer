using saas.Modules.Notes.Entities;

namespace saas.Modules.Notes.Services;

public interface INotesService
{
    Task<List<Note>> GetAllAsync();
    Task<Note?> GetByIdAsync(Guid id);
    Task<Note> CreateAsync(Note note);
    Task UpdateAsync(Guid id, Note note);
    Task DeleteAsync(Guid id);
    Task TogglePinAsync(Guid id);
}
