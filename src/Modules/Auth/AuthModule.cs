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
            })
            .AddCookie(AuthSchemes.Tenant, options =>
            {
                options.Cookie.Name = ".Tenant.Auth";
                options.LoginPath = "/{slug}/login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
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

    public void RegisterMvc(MvcOptions mvcOptions, IMvcBuilder mvcBuilder)
    {
    }
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
