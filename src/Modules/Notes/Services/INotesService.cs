using saas.Data;
using saas.Modules.Notes.Entities;

namespace saas.Modules.Notes.Services;

public interface INotesService
{
    Task<PaginatedList<Note>> GetAllAsync(int page = 1, int pageSize = 12);
    Task<Note?> GetByIdAsync(Guid id);
    Task<Note> CreateAsync(Note note);
    Task UpdateAsync(Guid id, Note note);
    Task DeleteAsync(Guid id);
    Task TogglePinAsync(Guid id);
}
