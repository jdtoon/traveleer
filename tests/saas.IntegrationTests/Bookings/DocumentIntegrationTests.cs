using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Bookings;

public class DocumentIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public DocumentIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }

    private async Task<Guid> SeedBookingAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.Select(c => c.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"ZZ-DOC-{Guid.NewGuid():N}"[..14],
            ClientId = clientId,
            Pax = 1,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<Guid> SeedClientAsync()
    {
        await using var db = OpenTenantDb();
        var client = new Client
        {
            Name = $"ZZ-DocClient-{Guid.NewGuid():N}"[..20],
            CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<(Guid BookingId, Guid DocumentId)> SeedBookingWithDocumentAsync()
    {
        await using var db = OpenTenantDb();
        var clientId = await db.Clients.Select(c => c.Id).FirstAsync();
        var booking = new Booking
        {
            BookingRef = $"ZZ-DOC-{Guid.NewGuid():N}"[..14],
            ClientId = clientId,
            Pax = 1,
            CostCurrencyCode = "USD",
            SellingCurrencyCode = "USD",
            CreatedAt = DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        var doc = new Document
        {
            BookingId = booking.Id,
            FileName = "test-voucher.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            StorageKey = $"demo/documents/{Guid.NewGuid():N}/test-voucher.pdf",
            DocumentType = DocumentType.Voucher,
            Description = "Seeded test document",
            UploadedBy = "test@demo.local",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (booking.Id, doc.Id);
    }

    private async Task<(Guid ClientId, Guid DocumentId)> SeedClientWithDocumentAsync()
    {
        await using var db = OpenTenantDb();
        var client = new Client
        {
            Name = $"ZZ-DocClient-{Guid.NewGuid():N}"[..20],
            CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        var doc = new Document
        {
            ClientId = client.Id,
            FileName = "passport-scan.jpg",
            ContentType = "image/jpeg",
            FileSize = 2048,
            StorageKey = $"demo/documents/{Guid.NewGuid():N}/passport-scan.jpg",
            DocumentType = DocumentType.Passport,
            Description = "Client passport",
            UploadedBy = "test@demo.local",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (client.Id, doc.Id);
    }

    private static async Task<string> ExtractAntiForgeryToken(HtmxTestResponse response)
    {
        var html = await response.GetContentAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (!match.Success)
            match = Regex.Match(html, @"value=""([^""]+)""\s*.*?name=""__RequestVerificationToken""");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    // ── Layer 1: Booking document list partial ──────────────────

    [Fact]
    public async Task BookingDocumentsPartial_RendersWithoutLayout()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/documents/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Documents");
        await response.AssertContainsAsync("No documents uploaded yet.");
    }

    [Fact]
    public async Task BookingDocumentsPartial_WithDocuments_ShowsList()
    {
        var (bookingId, _) = await SeedBookingWithDocumentAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/documents/{bookingId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("test-voucher.pdf");
        await response.AssertContainsAsync("Voucher");
        await response.AssertContainsAsync("Seeded test document");
    }

    // ── Layer 1: Client document list partial ───────────────────

    [Fact]
    public async Task ClientDocumentsPartial_RendersWithoutLayout()
    {
        var clientId = await SeedClientAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/documents/{clientId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No documents uploaded yet.");
    }

    [Fact]
    public async Task ClientDocumentsPartial_WithDocuments_ShowsList()
    {
        var (clientId, _) = await SeedClientWithDocumentAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/documents/{clientId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("passport-scan.jpg");
        await response.AssertContainsAsync("Passport");
    }

    // ── Layer 2: Upload form renders as modal ───────────────────

    [Fact]
    public async Task NewBookingDocument_RendersUploadModal()
    {
        var bookingId = await SeedBookingAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/documents/new/{bookingId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Upload Document");
        await response.AssertContainsAsync("hx-encoding=\"multipart/form-data\"");
    }

    [Fact]
    public async Task NewClientDocument_RendersUploadModal()
    {
        var clientId = await SeedClientAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/documents/new/{clientId}");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Upload Document");
    }

    // ── Layer 3: Upload flow ────────────────────────────────────

    [Fact]
    public async Task UploadBookingDocument_ValidFile_PersistsAndTriggers()
    {
        var bookingId = await SeedBookingAsync();

        // Get the form to extract antiforgery token
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/documents/new/{bookingId}");
        var token = await ExtractAntiForgeryToken(formResponse);

        // Build multipart content
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent(((int)DocumentType.Invoice).ToString()), "DocumentType");
        content.Add(new StringContent("Test upload invoice"), "Description");
        var fileContent = new ByteArrayContent(new byte[512]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", "integration-test.pdf");

        var response = await _client.HtmxPostAsync(
            $"/{TenantSlug}/bookings/documents/upload/{bookingId}", content);

        response.AssertSuccess();
        response.AssertTrigger("bookings.documents.refresh");

        // Verify in DB
        await using var db = OpenTenantDb();
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.BookingId == bookingId && d.FileName == "integration-test.pdf");
        Assert.NotNull(doc);
        Assert.Equal(DocumentType.Invoice, doc!.DocumentType);
        Assert.Equal("Test upload invoice", doc.Description);
        Assert.Equal(512, doc.FileSize);
        Assert.Contains("demo/documents/", doc.StorageKey);
    }

    [Fact]
    public async Task UploadClientDocument_ValidFile_PersistsAndTriggers()
    {
        var clientId = await SeedClientAsync();

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/documents/new/{clientId}");
        var token = await ExtractAntiForgeryToken(formResponse);

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent(((int)DocumentType.Passport).ToString()), "DocumentType");
        content.Add(new StringContent("Client passport scan"), "Description");
        var fileContent = new ByteArrayContent(new byte[256]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "File", "passport.jpg");

        var response = await _client.HtmxPostAsync(
            $"/{TenantSlug}/clients/documents/upload/{clientId}", content);

        response.AssertSuccess();
        response.AssertTrigger("clients.documents.refresh");

        await using var db = OpenTenantDb();
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.ClientId == clientId && d.FileName == "passport.jpg");
        Assert.NotNull(doc);
        Assert.Equal(DocumentType.Passport, doc!.DocumentType);
    }

    // ── Layer 3: Delete flow ────────────────────────────────────

    [Fact]
    public async Task DeleteBookingDocument_RemovesFromDatabase()
    {
        var (bookingId, docId) = await SeedBookingWithDocumentAsync();

        // Get the list to find the delete form
        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/documents/{bookingId}");
        var response = await _client.SubmitFormAsync(
            listResponse,
            $"form[hx-post='/{TenantSlug}/documents/delete/{docId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertTrigger("bookings.documents.refresh");

        await using var db = OpenTenantDb();
        Assert.False(await db.Documents.AnyAsync(d => d.Id == docId));
    }

    [Fact]
    public async Task DeleteClientDocument_RemovesFromDatabase()
    {
        var (clientId, docId) = await SeedClientWithDocumentAsync();

        var listResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/documents/{clientId}");
        var response = await _client.SubmitFormAsync(
            listResponse,
            $"form[hx-post='/{TenantSlug}/documents/delete/{docId}']",
            new Dictionary<string, string>());

        response.AssertSuccess();
        response.AssertTrigger("clients.documents.refresh");

        await using var db = OpenTenantDb();
        Assert.False(await db.Documents.AnyAsync(d => d.Id == docId));
    }

    // ── Layer 4: Access control ─────────────────────────────────

    [Fact]
    public async Task BookingDocuments_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/bookings/documents/{Guid.NewGuid()}");
        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // ── Layer 4: Non-existent parent ────────────────────────────

    [Fact]
    public async Task BookingDocuments_NonExistentBooking_Returns404()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/bookings/documents/{Guid.NewGuid()}");
        response.AssertStatus(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ClientDocuments_NonExistentClient_Returns404()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/clients/documents/{Guid.NewGuid()}");
        response.AssertStatus(System.Net.HttpStatusCode.NotFound);
    }
}
