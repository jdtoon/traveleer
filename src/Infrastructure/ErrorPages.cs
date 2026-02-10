namespace saas.Infrastructure;

/// <summary>
/// Standalone HTML error pages rendered by middleware before the MVC pipeline.
/// Extracted from TenantResolutionMiddleware for reuse and testability.
/// </summary>
public static class ErrorPages
{
    public static async Task Write404Async(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status404NotFound;
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Page Not Found</title>
                <style>
                    body { font-family: system-ui, -apple-system, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; background: #f5f5f5; color: #333; }
                    .container { text-align: center; padding: 2rem; }
                    h1 { font-size: 4rem; margin: 0; color: #888; }
                    p { font-size: 1.125rem; margin: 1rem 0; }
                    a { color: #6366f1; text-decoration: none; }
                    a:hover { text-decoration: underline; }
                </style>
            </head>
            <body>
                <div class="container">
                    <h1>404</h1>
                    <p>The page you're looking for doesn't exist.</p>
                    <a href="/">← Back to home</a>
                </div>
            </body>
            </html>
            """);
    }

    public static async Task Write403SuspendedAsync(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status403Forbidden;
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Account Suspended</title>
                <style>
                    body { font-family: system-ui, -apple-system, sans-serif; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; background: #fef2f2; color: #333; }
                    .container { text-align: center; padding: 2rem; max-width: 480px; }
                    h1 { font-size: 2.5rem; margin: 0; color: #dc2626; }
                    p { font-size: 1.125rem; margin: 1rem 0; color: #666; }
                    a { color: #6366f1; text-decoration: none; }
                    a:hover { text-decoration: underline; }
                </style>
            </head>
            <body>
                <div class="container">
                    <h1>Account Suspended</h1>
                    <p>This workspace has been suspended. Please contact support for assistance.</p>
                    <a href="/">← Back to home</a>
                </div>
            </body>
            </html>
            """);
    }
}
