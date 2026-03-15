using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Branding.Entities;
using saas.Modules.Portal.DTOs;
using saas.Modules.Portal.Entities;
using saas.Modules.Portal.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Portal.Controllers;

[Route("portal")]
public class PortalController : SwapController
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public PortalController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet("{tenantSlug}/{token}")]
    public async Task<IActionResult> Entry(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await service.CreateSessionAsync(link.Id, link.ClientId, ipAddress);

        return RedirectToAction(nameof(Dashboard), new { tenantSlug, token });
    }

    [HttpGet("{tenantSlug}/{token}/dashboard")]
    public async Task<IActionResult> Dashboard(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var branding = await GetBrandingAsync(db);
        var dashboard = await service.GetDashboardAsync(link.ClientId, branding);

        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        ViewBag.Scope = link.Scope;
        return SwapView("Dashboard", dashboard);
    }

    [HttpGet("{tenantSlug}/{token}/bookings")]
    public async Task<IActionResult> Bookings(string tenantSlug, string token, int page = 1, int pageSize = 12)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");
        if (link.Scope == PortalLinkScope.QuoteOnly) return NotFound();

        var bookings = await service.GetBookingsAsync(link.ClientId, page, pageSize);
        var branding = await GetBrandingAsync(db);

        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("Bookings", bookings);
    }

    [HttpGet("{tenantSlug}/{token}/bookings/{bookingId:guid}")]
    public async Task<IActionResult> BookingDetail(string tenantSlug, string token, Guid bookingId)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");
        if (link.Scope == PortalLinkScope.QuoteOnly) return NotFound();
        if (link.Scope == PortalLinkScope.BookingOnly && link.ScopedEntityId.HasValue && link.ScopedEntityId != bookingId)
            return NotFound();

        var booking = await service.GetBookingDetailAsync(link.ClientId, bookingId);
        if (booking is null) return NotFound();

        var branding = await GetBrandingAsync(db);

        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("BookingDetail", booking);
    }

    [HttpGet("{tenantSlug}/{token}/quotes")]
    public async Task<IActionResult> Quotes(string tenantSlug, string token, int page = 1, int pageSize = 12)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");
        if (link.Scope == PortalLinkScope.BookingOnly) return NotFound();

        var quotes = await service.GetQuotesAsync(link.ClientId, page, pageSize);
        var branding = await GetBrandingAsync(db);

        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("Quotes", quotes);
    }

    [HttpGet("{tenantSlug}/{token}/documents")]
    public async Task<IActionResult> Documents(string tenantSlug, string token, int page = 1, int pageSize = 12)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var documents = await service.GetDocumentsAsync(link.ClientId, page, pageSize);
        var branding = await GetBrandingAsync(db);

        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("Documents", documents);
    }

    [HttpGet("{tenantSlug}/{token}/documents/download/{docId:guid}")]
    public async Task<IActionResult> DownloadDocument(string tenantSlug, string token, Guid docId)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return NotFound();

        var doc = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docId && d.ClientId == link.ClientId);
        if (doc is null) return NotFound();

        var storageService = HttpContext.RequestServices.GetRequiredService<IStorageService>();
        var stream = await storageService.DownloadAsync(doc.StorageKey);
        if (stream is null) return NotFound();

        return File(stream, doc.ContentType, doc.FileName);
    }

    // ──── Client Self-Service Actions ────

    [HttpPost("{tenantSlug}/{token}/quotes/{quoteId:guid}/accept")]
    public async Task<IActionResult> AcceptQuote(string tenantSlug, string token, Guid quoteId)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var actionService = new ClientActionService(db);
        await actionService.SubmitActionAsync(new SubmitClientActionDto
        {
            ActionType = ClientActionType.AcceptQuote,
            EntityType = "Quote",
            EntityId = quoteId
        }, link.ClientId, null);

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("ActionConfirmation", new ClientActionDetailDto
        {
            ActionType = ClientActionType.AcceptQuote,
            EntityType = "Quote",
            EntityId = quoteId,
            ClientName = link.Client?.Name ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }

    [HttpPost("{tenantSlug}/{token}/quotes/{quoteId:guid}/decline")]
    public async Task<IActionResult> DeclineQuote(string tenantSlug, string token, Guid quoteId, [FromForm] string? notes)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var actionService = new ClientActionService(db);
        await actionService.SubmitActionAsync(new SubmitClientActionDto
        {
            ActionType = ClientActionType.DeclineQuote,
            EntityType = "Quote",
            EntityId = quoteId,
            Notes = notes
        }, link.ClientId, null);

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("ActionConfirmation", new ClientActionDetailDto
        {
            ActionType = ClientActionType.DeclineQuote,
            EntityType = "Quote",
            EntityId = quoteId,
            Notes = notes,
            ClientName = link.Client?.Name ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }

    [HttpGet("{tenantSlug}/{token}/quotes/{quoteId:guid}/change")]
    public async Task<IActionResult> RequestChangeForm(string tenantSlug, string token, Guid quoteId)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        ViewBag.QuoteId = quoteId;
        return SwapView("RequestChange");
    }

    [HttpPost("{tenantSlug}/{token}/quotes/{quoteId:guid}/change")]
    public async Task<IActionResult> RequestChange(string tenantSlug, string token, Guid quoteId, [FromForm] string? notes)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var actionService = new ClientActionService(db);
        await actionService.SubmitActionAsync(new SubmitClientActionDto
        {
            ActionType = ClientActionType.RequestChange,
            EntityType = "Quote",
            EntityId = quoteId,
            Notes = notes
        }, link.ClientId, null);

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("ActionConfirmation", new ClientActionDetailDto
        {
            ActionType = ClientActionType.RequestChange,
            EntityType = "Quote",
            EntityId = quoteId,
            Notes = notes,
            ClientName = link.Client?.Name ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }

    [HttpPost("{tenantSlug}/{token}/itinerary/{itineraryId:guid}/approve")]
    public async Task<IActionResult> ApproveItinerary(string tenantSlug, string token, Guid itineraryId)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var actionService = new ClientActionService(db);
        await actionService.SubmitActionAsync(new SubmitClientActionDto
        {
            ActionType = ClientActionType.ApproveItinerary,
            EntityType = "Itinerary",
            EntityId = itineraryId
        }, link.ClientId, null);

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("ActionConfirmation", new ClientActionDetailDto
        {
            ActionType = ClientActionType.ApproveItinerary,
            EntityType = "Itinerary",
            EntityId = itineraryId,
            ClientName = link.Client?.Name ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }

    [HttpGet("{tenantSlug}/{token}/feedback/{bookingId:guid}")]
    public async Task<IActionResult> FeedbackForm(string tenantSlug, string token, Guid bookingId)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        ViewBag.BookingId = bookingId;
        return SwapView("Feedback");
    }

    [HttpPost("{tenantSlug}/{token}/feedback/{bookingId:guid}")]
    public async Task<IActionResult> SubmitFeedback(string tenantSlug, string token, Guid bookingId, [FromForm] string? notes)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var portalService = new PortalService(db);
        var link = await portalService.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var actionService = new ClientActionService(db);
        await actionService.SubmitActionAsync(new SubmitClientActionDto
        {
            ActionType = ClientActionType.SubmitFeedback,
            EntityType = "Booking",
            EntityId = bookingId,
            Notes = notes
        }, link.ClientId, null);

        var branding = await GetBrandingAsync(db);
        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("ActionConfirmation", new ClientActionDetailDto
        {
            ActionType = ClientActionType.SubmitFeedback,
            EntityType = "Booking",
            EntityId = bookingId,
            Notes = notes,
            ClientName = link.Client?.Name ?? "",
            CreatedAt = DateTime.UtcNow
        });
    }

    private TenantDbContext? BuildTenantDb(string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug)) return null;

        var tenantPath = _configuration["Tenancy:DatabasePath"] ?? Path.Combine("db", "tenants");
        var basePath = Path.IsPathRooted(tenantPath)
            ? tenantPath
            : Path.Combine(_environment.ContentRootPath, tenantPath);
        var dbPath = Path.Combine(basePath, $"{tenantSlug}.db");

        if (!System.IO.File.Exists(dbPath)) return null;

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new TenantDbContext(options);
    }

    private static async Task<PortalBrandingDto> GetBrandingAsync(TenantDbContext db)
    {
        var settings = await db.BrandingSettings.AsNoTracking().FirstOrDefaultAsync();
        return new PortalBrandingDto
        {
            AgencyName = settings?.AgencyName ?? "Travel Agency",
            LogoUrl = settings?.LogoUrl,
            PrimaryColor = settings?.PrimaryColor ?? "#2563EB",
            SecondaryColor = settings?.SecondaryColor ?? "#1E3A5F",
            PrimaryTextColor = "#FFFFFF"
        };
    }
}
