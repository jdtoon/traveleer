using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.SuperAdmin.Services;
using Swap.Htmx;

namespace saas.Modules.SuperAdmin.Controllers;

[Authorize(Policy = "SuperAdmin")]
public class InfrastructureController : SwapController
{
    private readonly IInfrastructureService _infra;
    private readonly IConfiguration _configuration;

    public InfrastructureController(
        IInfrastructureService infra,
        IConfiguration configuration)
    {
        _infra = infra;
        _configuration = configuration;
    }

    // ── System Health Dashboard ──────────────────────────────────────────────

    [HttpGet("/super-admin/health")]
    public async Task<IActionResult> Health()
    {
        var health = await _infra.GetSystemHealthAsync();
        var disk = await _infra.GetDiskUsageAsync();
        var databases = await _infra.GetDatabaseSizesAsync();
        var hangfire = await _infra.GetHangfireStatusAsync();

        ViewBag.DiskUsage = disk;
        ViewBag.Databases = databases;
        ViewBag.Hangfire = hangfire;

        return SwapView(health);
    }

    [HttpGet("/super-admin/health/refresh")]
    public async Task<IActionResult> HealthRefresh()
    {
        var health = await _infra.GetSystemHealthAsync();
        var disk = await _infra.GetDiskUsageAsync();
        var databases = await _infra.GetDatabaseSizesAsync();
        var hangfire = await _infra.GetHangfireStatusAsync();

        ViewBag.DiskUsage = disk;
        ViewBag.Databases = databases;
        ViewBag.Hangfire = hangfire;

        return PartialView("_HealthContent", health);
    }

    // ── Redis Dashboard ──────────────────────────────────────────────────────

    [HttpGet("/super-admin/redis")]
    public async Task<IActionResult> Redis()
    {
        var redis = await _infra.GetRedisInfoAsync();
        return SwapView(redis);
    }

    [HttpGet("/super-admin/redis/refresh")]
    public async Task<IActionResult> RedisRefresh()
    {
        var redis = await _infra.GetRedisInfoAsync();
        return PartialView("_RedisContent", redis);
    }
}
