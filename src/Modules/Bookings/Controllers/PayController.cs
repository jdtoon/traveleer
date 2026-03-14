using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using Swap.Htmx;

namespace saas.Modules.Bookings.Controllers;

[Route("pay")]
public class PayController : SwapController
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public PayController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet("{tenantSlug}/{token}")]
    public async Task<IActionResult> Landing(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return SwapView("PayExpired");

        var service = new PaymentLinkService(db);
        var result = await service.GetByTokenAsync(token);
        if (result is null) return SwapView("PayExpired");
        if (result.Status != PaymentLinkStatus.Pending) return SwapView("PayExpired");

        return SwapView("PayLanding", result);
    }

    [HttpPost("{tenantSlug}/{token}/checkout")]
    public async Task<IActionResult> Checkout(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return SwapView("PayExpired");

        var service = new PaymentLinkService(db);
        var result = await service.GetByTokenAsync(token);
        if (result is null || result.Status != PaymentLinkStatus.Pending)
            return SwapView("PayExpired");

        // In dev mode: simulate payment directly (no real Stripe)
        var paid = await service.MarkAsPaidAsync(token);
        if (!paid) return SwapView("PayExpired");

        return RedirectToAction(nameof(Success), new { tenantSlug, token });
    }

    [HttpGet("{tenantSlug}/{token}/success")]
    public async Task<IActionResult> Success(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return SwapView("PayExpired");

        var service = new PaymentLinkService(db);
        var result = await service.GetByTokenAsync(token);
        if (result is null) return SwapView("PayExpired");

        return SwapView("PaySuccess", result);
    }

    [HttpGet("{tenantSlug}/{token}/cancel")]
    public async Task<IActionResult> PayCancel(string tenantSlug, string token)
    {
        await using var db = BuildTenantDb(tenantSlug);
        if (db is null) return SwapView("PayExpired");

        var service = new PaymentLinkService(db);
        var result = await service.GetByTokenAsync(token);
        if (result is null) return SwapView("PayExpired");

        return SwapView("PayCancel", result);
    }

    private TenantDbContext? BuildTenantDb(string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug)) return null;

        var tenantPath = _configuration["Tenancy:DatabasePath"] ?? System.IO.Path.Combine("db", "tenants");
        var basePath = System.IO.Path.IsPathRooted(tenantPath)
            ? tenantPath
            : System.IO.Path.Combine(_environment.ContentRootPath, tenantPath);
        var dbPath = System.IO.Path.Combine(basePath, $"{tenantSlug}.db");

        if (!System.IO.File.Exists(dbPath)) return null;

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new TenantDbContext(options);
    }
}
