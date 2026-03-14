using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using saas.Modules.Clients.Entities;
using saas.Shared;
using Xunit;

namespace saas.UnitTests.Modules.Bookings;

public class DocumentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly FakeStorageService _storage;
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();
        _storage = new FakeStorageService();
        var tenant = new FakeTenantContext("demo");
        _service = new DocumentService(_db, _storage, tenant);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<Guid> SeedBookingAsync()
    {
        var client = new Client { Name = "Doc Test Client", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        var booking = new Booking
        {
            BookingRef = $"BK-DOC-{Guid.NewGuid():N}"[..12],
            ClientId = client.Id,
            Pax = 1,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<Guid> SeedClientAsync()
    {
        var client = new Client { Name = "Doc Test Client", CreatedAt = DateTime.UtcNow };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client.Id;
    }

    private static IFormFile CreateFakeFile(string fileName = "test.pdf", string contentType = "application/pdf", int sizeBytes = 1024)
    {
        var stream = new MemoryStream(new byte[sizeBytes]);
        return new FormFile(stream, 0, sizeBytes, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    // ── Upload tests ─────────────────────────────────────────────

    [Fact]
    public async Task Upload_WithValidFile_PersistsDocument()
    {
        var bookingId = await SeedBookingAsync();
        var dto = new DocumentUploadDto
        {
            BookingId = bookingId,
            File = CreateFakeFile("invoice.pdf", "application/pdf", 2048),
            DocumentType = DocumentType.Invoice,
            Description = "Test invoice"
        };

        var id = await _service.UploadAsync(dto, "test@local");

        var doc = await _db.Documents.SingleAsync(d => d.Id == id);
        Assert.Equal("invoice.pdf", doc.FileName);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal(2048, doc.FileSize);
        Assert.Equal(DocumentType.Invoice, doc.DocumentType);
        Assert.Equal("Test invoice", doc.Description);
        Assert.Equal("test@local", doc.UploadedBy);
        Assert.Equal(bookingId, doc.BookingId);
        Assert.Contains("demo/documents/", doc.StorageKey);
        Assert.Contains(doc.StorageKey, _storage.UploadedPaths);
    }

    [Fact]
    public async Task Upload_WithClientId_PersistsClientDocument()
    {
        var clientId = await SeedClientAsync();
        var dto = new DocumentUploadDto
        {
            ClientId = clientId,
            File = CreateFakeFile("passport.jpg", "image/jpeg"),
            DocumentType = DocumentType.Passport
        };

        var id = await _service.UploadAsync(dto, "agent@local");

        var doc = await _db.Documents.SingleAsync(d => d.Id == id);
        Assert.Equal(clientId, doc.ClientId);
        Assert.Null(doc.BookingId);
        Assert.Equal(DocumentType.Passport, doc.DocumentType);
    }

    [Fact]
    public async Task Upload_BlockedExtension_Throws()
    {
        var bookingId = await SeedBookingAsync();
        var dto = new DocumentUploadDto
        {
            BookingId = bookingId,
            File = CreateFakeFile("malware.exe"),
            DocumentType = DocumentType.Other
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UploadAsync(dto, "test@local"));
        Assert.Contains(".exe", ex.Message);
    }

    [Fact]
    public async Task Upload_OversizedFile_Throws()
    {
        var bookingId = await SeedBookingAsync();
        var dto = new DocumentUploadDto
        {
            BookingId = bookingId,
            File = CreateFakeFile("huge.pdf", "application/pdf", 11 * 1024 * 1024),
            DocumentType = DocumentType.Other
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UploadAsync(dto, "test@local"));
        Assert.Contains("10 MB", ex.Message);
    }

    [Fact]
    public async Task Upload_EmptyFile_Throws()
    {
        var bookingId = await SeedBookingAsync();
        var dto = new DocumentUploadDto
        {
            BookingId = bookingId,
            File = CreateFakeFile("empty.pdf", "application/pdf", 0),
            DocumentType = DocumentType.Other
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UploadAsync(dto, "test@local"));
        Assert.Contains("No file", ex.Message);
    }

    [Fact]
    public async Task Upload_NullFile_Throws()
    {
        var bookingId = await SeedBookingAsync();
        var dto = new DocumentUploadDto
        {
            BookingId = bookingId,
            File = null,
            DocumentType = DocumentType.Other
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UploadAsync(dto, "test@local"));
    }

    // ── List tests ───────────────────────────────────────────────

    [Fact]
    public async Task GetBookingDocuments_ReturnsDocumentsForBooking()
    {
        var bookingId = await SeedBookingAsync();
        _db.Documents.Add(new Document
        {
            BookingId = bookingId,
            FileName = "voucher.pdf",
            ContentType = "application/pdf",
            FileSize = 500,
            StorageKey = "demo/documents/x/voucher.pdf",
            DocumentType = DocumentType.Voucher,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetBookingDocumentsAsync(bookingId);

        Assert.NotNull(result);
        Assert.Single(result!.Documents);
        Assert.Equal("voucher.pdf", result.Documents[0].FileName);
        Assert.Equal(DocumentType.Voucher, result.Documents[0].DocumentType);
    }

    [Fact]
    public async Task GetBookingDocuments_NonExistentBooking_ReturnsNull()
    {
        var result = await _service.GetBookingDocumentsAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetClientDocuments_ReturnsDocumentsForClient()
    {
        var clientId = await SeedClientAsync();
        _db.Documents.Add(new Document
        {
            ClientId = clientId,
            FileName = "passport.jpg",
            ContentType = "image/jpeg",
            FileSize = 3000,
            StorageKey = "demo/documents/y/passport.jpg",
            DocumentType = DocumentType.Passport,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetClientDocumentsAsync(clientId);

        Assert.NotNull(result);
        Assert.Single(result!.Documents);
        Assert.Equal("passport.jpg", result.Documents[0].FileName);
    }

    [Fact]
    public async Task GetClientDocuments_NonExistentClient_ReturnsNull()
    {
        var result = await _service.GetClientDocumentsAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── Delete tests ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingDocument_RemovesFromDbAndStorage()
    {
        var bookingId = await SeedBookingAsync();
        var doc = new Document
        {
            BookingId = bookingId,
            FileName = "delete-me.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            StorageKey = "demo/documents/z/delete-me.pdf",
            DocumentType = DocumentType.Other,
            CreatedAt = DateTime.UtcNow
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        var result = await _service.DeleteAsync(doc.Id);

        Assert.True(result);
        Assert.False(await _db.Documents.AnyAsync(d => d.Id == doc.Id));
        Assert.Contains("demo/documents/z/delete-me.pdf", _storage.DeletedPaths);
    }

    [Fact]
    public async Task Delete_NonExistentDocument_ReturnsFalse()
    {
        var result = await _service.DeleteAsync(Guid.NewGuid());
        Assert.False(result);
    }

    // ── GetDocument test ─────────────────────────────────────────

    [Fact]
    public async Task GetDocument_ReturnsEntityById()
    {
        var bookingId = await SeedBookingAsync();
        var doc = new Document
        {
            BookingId = bookingId,
            FileName = "find-me.pdf",
            ContentType = "application/pdf",
            FileSize = 200,
            StorageKey = "demo/documents/w/find-me.pdf",
            DocumentType = DocumentType.Invoice,
            CreatedAt = DateTime.UtcNow
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        var result = await _service.GetDocumentAsync(doc.Id);

        Assert.NotNull(result);
        Assert.Equal("find-me.pdf", result!.FileName);
        Assert.Equal("demo/documents/w/find-me.pdf", result.StorageKey);
    }

    // ── Blocked extensions coverage ──────────────────────────────

    [Theory]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".ps1")]
    [InlineData(".sh")]
    [InlineData(".dll")]
    [InlineData(".vbs")]
    [InlineData(".scr")]
    public async Task Upload_VariousBlockedExtensions_Throws(string ext)
    {
        var bookingId = await SeedBookingAsync();
        var dto = new DocumentUploadDto
        {
            BookingId = bookingId,
            File = CreateFakeFile($"file{ext}"),
            DocumentType = DocumentType.Other
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UploadAsync(dto, "test@local"));
    }

    // ── Test doubles ─────────────────────────────────────────────

    private class FakeStorageService : IStorageService
    {
        public List<string> UploadedPaths { get; } = [];
        public List<string> DeletedPaths { get; } = [];

        public Task<string> UploadAsync(Stream stream, string path, string contentType, CancellationToken ct = default)
        {
            UploadedPaths.Add(path);
            return Task.FromResult(path);
        }

        public Task<Stream?> DownloadAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream?>(new MemoryStream(new byte[10]));

        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeletedPaths.Add(path);
            return Task.FromResult(true);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> GetUrlAsync(string path, TimeSpan? expiry = null, CancellationToken ct = default)
            => Task.FromResult<string?>($"https://fake/{path}");
    }

    private class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(string slug) => Slug = slug;
        public string? Slug { get; }
        public Guid? TenantId => Guid.NewGuid();
        public string? PlanSlug => "starter";
        public string? TenantName => "Test Tenant";
        public bool IsTenantRequest => true;
    }
}
