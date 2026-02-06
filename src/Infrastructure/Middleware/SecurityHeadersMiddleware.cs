namespace saas.Infrastructure.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        // Content Security Policy (allow Turnstile if enabled later)
        headers["Content-Security-Policy"] = "default-src 'self'; " +
                                             "img-src 'self' data:; " +
                                             "style-src 'self' 'unsafe-inline'; " +
                                             "script-src 'self' 'unsafe-inline'; " +
                                             "frame-src https://challenges.cloudflare.com";

        await _next(context);
    }
}
