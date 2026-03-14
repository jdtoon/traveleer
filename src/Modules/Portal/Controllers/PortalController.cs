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
    public async Task<IActionResult> Bookings(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");
        if (link.Scope == PortalLinkScope.QuoteOnly) return NotFound();

        var bookings = await service.GetBookingsAsync(link.ClientId);
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
    public async Task<IActionResult> Quotes(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");
        if (link.Scope == PortalLinkScope.BookingOnly) return NotFound();

        var quotes = await service.GetQuotesAsync(link.ClientId);
        var branding = await GetBrandingAsync(db);

        ViewBag.Token = token;
        ViewBag.TenantSlug = tenantSlug;
        ViewBag.Branding = branding;
        return SwapView("Quotes", quotes);
    }

    [HttpGet("{tenantSlug}/{token}/documents")]
    public async Task<IActionResult> Documents(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return NotFound();

        var service = new PortalService(db);
        var link = await service.ValidateTokenAsync(token);
        if (link is null) return SwapView("Expired");

        var documents = await service.GetDocumentsAsync(link.ClientId);
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
