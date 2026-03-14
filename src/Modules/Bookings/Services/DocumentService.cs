using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Shared;

namespace saas.Modules.Bookings.Services;

public interface IDocumentService
{
    Task<DocumentListDto?> GetBookingDocumentsAsync(Guid bookingId);
    Task<DocumentListDto?> GetClientDocumentsAsync(Guid clientId);
    Task<Document?> GetDocumentAsync(Guid id);
    Task<Guid> UploadAsync(DocumentUploadDto dto, string? uploadedBy);
    Task<bool> DeleteAsync(Guid id);
}

public class DocumentService : IDocumentService
{
    private readonly TenantDbContext _db;
    private readonly IStorageService _storage;
    private readonly ITenantContext _tenant;

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".sh", ".dll", ".com", ".msi",
        ".vbs", ".js", ".wsf", ".scr", ".pif", ".hta", ".cpl", ".inf"
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public DocumentService(TenantDbContext db, IStorageService storage, ITenantContext tenant)
    {
        _db = db;
        _storage = storage;
        _tenant = tenant;
    }

    public async Task<DocumentListDto?> GetBookingDocumentsAsync(Guid bookingId)
    {
        var booking = await _db.Bookings
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.Id, b.BookingRef })
            .FirstOrDefaultAsync();

        if (booking is null) return null;

        var docs = await _db.Documents
            .Where(d => d.BookingId == bookingId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentItemDto
            {
                Id = d.Id,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSize = d.FileSize,
                DocumentType = d.DocumentType,
                Description = d.Description,
                UploadedBy = d.UploadedBy,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return new DocumentListDto
        {
            BookingId = bookingId,
            ParentName = booking.BookingRef,
            Documents = docs
        };
    }

    public async Task<DocumentListDto?> GetClientDocumentsAsync(Guid clientId)
    {
        var client = await _db.Clients
            .Where(c => c.Id == clientId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync();

        if (client is null) return null;

        var docs = await _db.Documents
            .Where(d => d.ClientId == clientId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentItemDto
            {
                Id = d.Id,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSize = d.FileSize,
                DocumentType = d.DocumentType,
                Description = d.Description,
                UploadedBy = d.UploadedBy,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return new DocumentListDto
        {
            ClientId = clientId,
            ParentName = client.Name,
            Documents = docs
        };
    }

    public async Task<Document?> GetDocumentAsync(Guid id)
    {
        return await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Guid> UploadAsync(DocumentUploadDto dto, string? uploadedBy)
    {
        if (dto.File is null || dto.File.Length == 0)
            throw new InvalidOperationException("No file provided.");

        if (dto.File.Length > MaxFileSize)
            throw new InvalidOperationException($"File exceeds the {MaxFileSize / (1024 * 1024)} MB limit.");

        var ext = Path.GetExtension(dto.File.FileName);
        if (BlockedExtensions.Contains(ext))
            throw new InvalidOperationException($"File type '{ext}' is not allowed.");

        var docId = Guid.NewGuid();
        var safeFileName = Path.GetFileName(dto.File.FileName); // strip path info
        var storageKey = $"{_tenant.Slug}/documents/{docId:N}/{safeFileName}";

        await using var stream = dto.File.OpenReadStream();
        await _storage.UploadAsync(stream, storageKey, dto.File.ContentType);

        var document = new Document
        {
            Id = docId,
            BookingId = dto.BookingId,
            ClientId = dto.ClientId,
            FileName = safeFileName,
            ContentType = dto.File.ContentType,
            FileSize = dto.File.Length,
            StorageKey = storageKey,
            DocumentType = dto.DocumentType,
            Description = dto.Description,
            UploadedBy = uploadedBy
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync();

        return docId;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc is null) return false;

        await _storage.DeleteAsync(doc.StorageKey);
        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();
        return true;
    }
}
