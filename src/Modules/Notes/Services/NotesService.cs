using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Notes.Entities;

namespace saas.Modules.Notes.Services;

public class NotesService : INotesService
{
    private readonly TenantDbContext _db;

    public NotesService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<Note>> GetAllAsync()
    {
        return await _db.Notes
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    public async Task<Note?> GetByIdAsync(Guid id)
    {
        return await _db.Notes.FindAsync(id);
    }

    public async Task<Note> CreateAsync(Note note)
    {
        _db.Notes.Add(note);
        await _db.SaveChangesAsync();
        return note;
    }

    public async Task UpdateAsync(Guid id, Note note)
    {
        var existing = await _db.Notes.FindAsync(id)
            ?? throw new InvalidOperationException($"Note {id} not found");

        existing.Title = note.Title;
        existing.Content = note.Content;
        existing.Color = note.Color;

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var note = await _db.Notes.FindAsync(id)
            ?? throw new InvalidOperationException($"Note {id} not found");

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();
    }

    public async Task TogglePinAsync(Guid id)
    {
        var note = await _db.Notes.FindAsync(id)
            ?? throw new InvalidOperationException($"Note {id} not found");

        note.IsPinned = !note.IsPinned;
        await _db.SaveChangesAsync();
    }
}
