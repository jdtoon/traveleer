using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Itineraries.Services;
using Swap.Htmx;

namespace saas.Modules.Itineraries.Controllers;

[Route("shared/itinerary")]
public class SharedItineraryController : SwapController
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public SharedItineraryController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet("{tenantSlug}/{token}")]
    public new async Task<IActionResult> View(string tenantSlug, string token)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(token))
            return NotFound();

        var tenantPath = _configuration["Tenancy:DatabasePath"] ?? Path.Combine("db", "tenants");
        var basePath = Path.IsPathRooted(tenantPath)
            ? tenantPath
            : Path.Combine(_environment.ContentRootPath, tenantPath);
        var dbPath = Path.Combine(basePath, $"{tenantSlug}.db");

        if (!System.IO.File.Exists(dbPath))
            return NotFound();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var db = new TenantDbContext(options);
        var service = new ItineraryService(db);
        var model = await service.GetByShareTokenAsync(token);
        return model is null ? NotFound() : SwapView("SharedView", model);
    }
}
