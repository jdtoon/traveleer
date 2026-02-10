namespace saas.Infrastructure;

/// <summary>
/// Standalone HTML error pages rendered by middleware before the MVC pipeline.
/// Reads HTML from wwwroot/errors/ files and caches the content.
/// </summary>
public static class ErrorPages
{
    private static string? _cached404;
    private static string? _cached403;

    /// <summary>
    /// Initialise the cache by reading HTML files from wwwroot/errors/.
    /// Call once at startup after WebApplication is built.
    /// </summary>
    public static void Initialize(IWebHostEnvironment env)
    {
        var errorsDir = Path.Combine(env.WebRootPath, "errors");
        var path404 = Path.Combine(errorsDir, "404.html");
        var path403 = Path.Combine(errorsDir, "403-suspended.html");

        _cached404 = File.Exists(path404)
            ? File.ReadAllText(path404)
            : "<h1>404 — Not Found</h1>";

        _cached403 = File.Exists(path403)
            ? File.ReadAllText(path403)
            : "<h1>403 — Account Suspended</h1>";
    }

    public static async Task Write404Async(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status404NotFound;
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(_cached404 ?? "<h1>404 — Not Found</h1>");
    }

    public static async Task Write403SuspendedAsync(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status403Forbidden;
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(_cached403 ?? "<h1>403 — Account Suspended</h1>");
    }
}
