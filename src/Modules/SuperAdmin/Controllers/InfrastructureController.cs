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
    private readonly IHttpClientFactory _httpClientFactory;

    public InfrastructureController(
        IInfrastructureService infra,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _infra = infra;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
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

    // ── RabbitMQ Dashboard ────────────────────────────────────────────────────

    [HttpGet("/super-admin/rabbitmq")]
    public async Task<IActionResult> RabbitMQ()
    {
        var status = await _infra.GetRabbitMqStatusAsync();
        ViewBag.ManagementUrl = _configuration["Infrastructure:RabbitMqManagementUrl"];
        return SwapView(status);
    }

    [HttpGet("/super-admin/rabbitmq/refresh")]
    public async Task<IActionResult> RabbitMqRefresh()
    {
        var status = await _infra.GetRabbitMqStatusAsync();
        ViewBag.ManagementUrl = _configuration["Infrastructure:RabbitMqManagementUrl"];
        return PartialView("_RabbitMqContent", status);
    }

    // ── Seq Logs Dashboard ────────────────────────────────────────────────────

    [HttpGet("/super-admin/logs")]
    public IActionResult Logs()
    {
        ViewBag.SeqUrl = _configuration["Infrastructure:SeqUrl"];
        return SwapView();
    }

    // ── Uptime Kuma Dashboard ─────────────────────────────────────────────────

    [HttpGet("/super-admin/uptime")]
    public IActionResult Uptime()
    {
        ViewBag.UptimeKumaUrl = _configuration["Infrastructure:UptimeKumaUrl"];
        return SwapView();
    }

    // ── Jobs Dashboard (Item 12) ─────────────────────────────────────────────

    [HttpGet("/super-admin/jobs")]
    public async Task<IActionResult> Jobs()
    {
        var hangfire = await _infra.GetHangfireStatusAsync();
        return SwapView(hangfire);
    }

    [HttpGet("/super-admin/jobs/refresh")]
    public async Task<IActionResult> JobsRefresh()
    {
        var hangfire = await _infra.GetHangfireStatusAsync();
        return PartialView("_JobsContent", hangfire);
    }

    // ── Reverse Proxy (Seq & Uptime Kuma) ───────────────────────────────────────
    // These services set X-Frame-Options headers that block iframe embedding.
    // Proxying through our app keeps them same-origin and strips those headers.

    [Route("/super-admin/proxy/seq/{**path}")]
    public Task ProxySeq(string? path)
        => ProxyTo(_configuration["Infrastructure:SeqUrl"], path);

    [Route("/super-admin/proxy/uptime/{**path}")]
    public Task ProxyUptime(string? path)
        => ProxyTo(_configuration["Infrastructure:UptimeKumaUrl"], path);

    private static readonly HashSet<string> StrippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Connection", "Keep-Alive",
        "X-Frame-Options", "Content-Security-Policy", "X-Content-Security-Policy"
    };

    private async Task ProxyTo(string? baseUrl, string? path)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            Response.StatusCode = 502;
            await Response.WriteAsync("Service not configured");
            return;
        }

        var targetUri = $"{baseUrl.TrimEnd('/')}/{path}{Request.QueryString}";

        using var request = new HttpRequestMessage(new HttpMethod(Request.Method), targetUri);

        // Forward request body for non-GET methods
        if (Request.ContentLength > 0 || Request.ContentType is not null)
        {
            request.Content = new StreamContent(Request.Body);
            if (Request.ContentType is not null)
                request.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(Request.ContentType);
        }

        // Forward Accept header so upstream returns correct content type
        if (Request.Headers.TryGetValue("Accept", out var accept))
            request.Headers.TryAddWithoutValidation("Accept", accept.ToString());

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            Response.StatusCode = (int)response.StatusCode;

            // Copy response headers, stripping hop-by-hop and frame-blocking ones
            foreach (var (key, values) in response.Headers.Concat(response.Content.Headers))
            {
                if (StrippedHeaders.Contains(key)) continue;
                Response.Headers[key] = values.ToArray();
            }

            await response.Content.CopyToAsync(Response.Body);
        }
        catch (HttpRequestException)
        {
            Response.StatusCode = 502;
            Response.ContentType = "text/html";
            await Response.WriteAsync(
                "<div style='padding:2rem;text-align:center;font-family:sans-serif;color:#888'>" +
                "<h2>Service Unavailable</h2>" +
                "<p>The upstream service is not reachable. Make sure Docker containers are running.</p></div>");
        }
        catch (TaskCanceledException)
        {
            Response.StatusCode = 504;
            Response.ContentType = "text/html";
            await Response.WriteAsync(
                "<div style='padding:2rem;text-align:center;font-family:sans-serif;color:#888'>" +
                "<h2>Gateway Timeout</h2>" +
                "<p>The upstream service did not respond in time.</p></div>");
        }
    }
}
