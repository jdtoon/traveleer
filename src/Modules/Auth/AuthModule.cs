using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Modules.Auth.Services;
using saas.Shared;

namespace saas.Modules.Auth;

public class AuthModule : IModule
{
    public string Name => "Auth";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["TenantAuth"] = "Auth",
        ["SuperAdminAuth"] = "Auth",
        ["Profile"] = "Auth",
        ["TwoFactor"] = "Auth",
        ["SuperAdminTwoFactor"] = "Auth",
        ["Session"] = "Auth"
    };

    public IReadOnlyList<string> PublicRoutePrefixes =>
    [
        "login", "login-redirect", "login-modal"
    ];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        // SSO removed — no implementation exists
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<MagicLinkService>();
        services.AddScoped<EmailVerificationService>();
        services.AddScoped<TwoFactorService>();
        services.AddScoped<SuperAdminTwoFactorService>();
        services.TryAddScoped<ICurrentUser, CurrentUser>();
        services.AddHostedService<MagicLinkCleanupService>();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<TenantDbContext>()
            .AddSignInManager();

        services.AddAuthentication()
            .AddCookie(AuthSchemes.SuperAdmin, options =>
            {
                options.Cookie.Name = ".SuperAdmin.Auth";
                options.LoginPath = "/super-admin/login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(24);
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
            })
            .AddCookie(AuthSchemes.Tenant, options =>
            {
                options.Cookie.Name = ".Tenant.Auth";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // LoginPath is not used directly — we handle redirects dynamically
                // to inject the actual tenant slug from the request URL.
                options.LoginPath = "/login-redirect";

                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        var tenantSlug = ExtractTenantSlug(context.Request.Path);
                        if (!string.IsNullOrEmpty(tenantSlug))
                        {
                            var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                            context.RedirectUri = $"/{tenantSlug}/login?returnUrl={returnUrl}";
                        }
                        else
                        {
                            context.RedirectUri = "/login-redirect";
                        }

                        if (IsHtmxRequest(context.HttpContext.Request))
                        {
                            // HTMX: respond with HX-Redirect header instead of 302
                            context.HttpContext.Response.StatusCode = 200;
                            context.HttpContext.Response.Headers["HX-Redirect"] = context.RedirectUri;
                            return Task.CompletedTask;
                        }

                        context.HttpContext.Response.Redirect(context.RedirectUri);
                        context.HttpContext.Response.StatusCode = StatusCodes.Status302Found;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        var tenantSlug = ExtractTenantSlug(context.Request.Path);
                        context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("SuperAdmin", policy =>
            {
                policy.AddAuthenticationSchemes(AuthSchemes.SuperAdmin);
                policy.RequireClaim(AuthClaims.IsSuperAdmin, "true");
            });

            options.AddPolicy("TenantUser", policy =>
            {
                policy.AddAuthenticationSchemes(AuthSchemes.Tenant);
                policy.RequireClaim(AuthClaims.TenantSlug);
            });

            options.AddPolicy("TenantAdmin", policy =>
            {
                policy.AddAuthenticationSchemes(AuthSchemes.Tenant);
                policy.RequireClaim(AuthClaims.TenantSlug);
                policy.RequireRole("Admin");
            });
        });
    }

    public void RegisterMiddleware(IApplicationBuilder app)
    {
    }

    /// <summary>
    /// Extracts the first URL segment as a potential tenant slug.
    /// Used by cookie event handlers to build correct redirect URLs.
    /// </summary>
    private static string? ExtractTenantSlug(PathString path)
    {
        var segments = path.Value?.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is { Length: > 0 } ? segments[0] : null;
    }

    private static bool IsHtmxRequest(HttpRequest request)
        => request.Headers.ContainsKey("HX-Request");
}

public static class AuthSchemes
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Tenant = "Tenant";
}

public static class AuthClaims
{
    public const string IsSuperAdmin = "is_super_admin";
    public const string TenantSlug = "tenant_slug";
    public const string Permission = "permission";
    public const string SessionId = "session_id";
}
