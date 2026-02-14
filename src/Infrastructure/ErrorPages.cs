namespace saas.Infrastructure;

/// <summary>
/// Standalone HTML error pages rendered by middleware before the MVC pipeline.
/// Reads HTML from wwwroot/errors/ files and caches the content.
/// </summary>
public static class ErrorPages
{
    private static string? _cached404;
    private static string? _cached403;
    private static string? _cached429;

    /// <summary>
    /// Initialise the cache by reading HTML files from wwwroot/errors/.
    /// Call once at startup after WebApplication is built.
    /// </summary>
    public static void Initialize(IWebHostEnvironment env)
    {
        var errorsDir = Path.Combine(env.WebRootPath, "errors");
        var path404 = Path.Combine(errorsDir, "404.html");
        var path403 = Path.Combine(errorsDir, "403-suspended.html");
        var path429 = Path.Combine(errorsDir, "429.html");

        _cached404 = File.Exists(path404)
            ? File.ReadAllText(path404)
            : "<h1>404 — Not Found</h1>";

        _cached403 = File.Exists(path403)
            ? File.ReadAllText(path403)
            : "<h1>403 — Account Suspended</h1>";

        _cached429 = File.Exists(path429)
            ? File.ReadAllText(path429)
            : "<h1>429 — Too Many Requests</h1><p>Please slow down and try again shortly.</p>";
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

    public static async Task Write429Async(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(_cached429 ?? "<h1>429 — Too Many Requests</h1>");
    }

    /// <summary>
    /// Returns a short HTML snippet suitable for htmx swap targets (toast alert).
    /// </summary>
    public static string Get429Toast()
    {
        return """
            <div class="alert alert-warning shadow-lg" role="alert">
                <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
                <span>Too many requests — please wait a moment and try again.</span>
            </div>
            """;
    }
}
