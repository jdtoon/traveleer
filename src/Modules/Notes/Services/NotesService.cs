using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Notes.Entities;

namespace saas.Modules.Notes.Services;

public class NotesService
{
    private readonly TenantDbContext _db;

    public NotesService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<PaginatedList<Note>> GetAllAsync(int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Notes
            .AsNoTracking()
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt);

        return await PaginatedList<Note>.CreateAsync(query, page, pageSize);
    }

    public async Task<Note> CreateAsync(Note note)
    {
        if (string.IsNullOrWhiteSpace(note.Title))
            throw new InvalidOperationException("Note title is required.");

        note.Id = note.Id == Guid.Empty ? Guid.NewGuid() : note.Id;
        note.Title = note.Title.Trim();
        note.Content = Normalize(note.Content);
        note.Color = string.IsNullOrWhiteSpace(note.Color) ? "gray" : note.Color.Trim();

        _db.Notes.Add(note);
        await _db.SaveChangesAsync();
        return note;
    }

    public async Task<Note> UpdateAsync(Guid id, Note updated)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id)
            ?? throw new InvalidOperationException($"Note {id} not found");

        note.Title = string.IsNullOrWhiteSpace(updated.Title)
            ? throw new InvalidOperationException("Note title is required.")
            : updated.Title.Trim();
        note.Content = Normalize(updated.Content);
        note.Color = string.IsNullOrWhiteSpace(updated.Color) ? note.Color : updated.Color.Trim();

        await _db.SaveChangesAsync();
        return note;
    }

    public async Task DeleteAsync(Guid id)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id)
            ?? throw new InvalidOperationException($"Note {id} not found");

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();
    }

    public async Task TogglePinAsync(Guid id)
    {
        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == id)
            ?? throw new InvalidOperationException($"Note {id} not found");

        note.IsPinned = !note.IsPinned;
        await _db.SaveChangesAsync();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}