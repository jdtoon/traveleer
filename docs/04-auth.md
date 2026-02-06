# 04 — Authentication & Authorization

> Defines the dual authentication system (super admin + tenant users), magic link flow, ASP.NET Identity integration, RBAC with permissions, and tag helpers for view-level access control.

**Prerequisites**: [01 — Architecture](01-architecture.md), [02 — Database & Multi-Tenancy](02-database-multitenancy.md), [03 — Modules](03-modules.md)

---

## 1. Authentication Strategy Overview

The application has **two completely separate authentication contexts**:

| Context | Who | Auth Method | Cookie | Database | Identity System |
|---------|-----|-------------|--------|----------|----------------|
| **Super Admin** | SaaS owner/operator | Magic link (email) | `.SuperAdmin.Auth` | `core.db` | Custom (no ASP.NET Identity) |
| **Tenant User** | Tenant's employees | Magic link (email) | `.Tenant.Auth` | `tenants/{slug}.db` | ASP.NET Identity |

### Why Magic Link for Everything?

- **Zero password management** — no hashing, no reset flows, no breach liability
- **Frictionless UX** — click a link, you're in (Linear, Slack, Notion all do this)
- **Simpler codebase** — no password validation, no strength requirements, no "forgot password" module
- **Security** — phishing-resistant (token is one-time, short-lived), no credential stuffing

### Authentication Flow Diagram

```
                    ┌──────────────┐
                    │  User visits  │
                    │  login page   │
                    └──────┬───────┘
                           │
              ┌────────────┴────────────┐
              │                         │
      /login (super admin)    /{tenant}/login
              │                         │
              ▼                         ▼
    ┌─────────────────┐     ┌─────────────────┐
    │ Enter email      │     │ Enter email      │
    │ + Turnstile      │     │ + Turnstile      │
    └────────┬────────┘     └────────┬────────┘
             │                       │
             ▼                       ▼
    ┌─────────────────┐     ┌─────────────────┐
    │ Verify email is  │     │ Verify email     │
    │ in SuperAdmins   │     │ exists in tenant │
    │ table (core.db)  │     │ Identity DB      │
    └────────┬────────┘     └────────┬────────┘
             │                       │
             ▼                       ▼
    ┌─────────────────────────────────────────┐
    │         Generate Magic Link Token         │
    │  - Crypto-random token (32 bytes)         │
    │  - Hash with SHA256 before storing        │
    │  - Store in MagicLinkTokens (core.db)     │
    │  - TTL: 15 minutes (configurable)         │
    └────────────────┬────────────────────────┘
                     │
                     ▼
    ┌─────────────────────────────────────────┐
    │          Deliver Magic Link               │
    │  Production: IEmailService (AWS SES)      │
    │  Local dev:  Console logger               │
    └────────────────┬────────────────────────┘
                     │
                     ▼
    ┌─────────────────────────────────────────┐
    │         User clicks link                  │
    │  /magic-link/{token}  or                  │
    │  /{tenant}/magic-link/{token}             │
    └────────────────┬────────────────────────┘
                     │
                     ▼
    ┌─────────────────────────────────────────┐
    │         Verify Token                      │
    │  - Hash the URL token                     │
    │  - Look up in MagicLinkTokens             │
    │  - Check not expired, not used            │
    │  - Mark as used                           │
    └────────────────┬────────────────────────┘
                     │
                     ▼
    ┌─────────────────────────────────────────┐
    │         Issue Auth Cookie                 │
    │  - Create ClaimsPrincipal with claims     │
    │  - Sign in with appropriate scheme        │
    │  - Redirect to dashboard                  │
    └─────────────────────────────────────────┘
```

---

## 2. Cookie Authentication Schemes

Two named authentication schemes, both cookie-based:

```csharp
public static class AuthSchemes
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Tenant = "Tenant";
}
```

### Registration

```csharp
// In AuthModule.RegisterServices()
services.AddAuthentication()
    .AddCookie(AuthSchemes.SuperAdmin, options =>
    {
        options.Cookie.Name = ".SuperAdmin.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
    })
    .AddCookie(AuthSchemes.Tenant, options =>
    {
        options.Cookie.Name = ".Tenant.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;

        // Dynamic login path based on tenant slug
        options.Events.OnRedirectToLogin = context =>
        {
            var tenantContext = context.HttpContext.RequestServices
                .GetRequiredService<ITenantContext>();

            context.RedirectUri = tenantContext.IsTenantRequest
                ? $"/{tenantContext.Slug}/login"
                : "/login";

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
```

### Authorization Policies

```csharp
services.AddAuthorization(options =>
{
    // Super admin policy — requires SuperAdmin scheme + IsSuperAdmin claim
    options.AddPolicy("SuperAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add(AuthSchemes.SuperAdmin);
        policy.RequireClaim("IsSuperAdmin", "true");
    });

    // Tenant user policy — requires Tenant scheme + active tenant
    options.AddPolicy("TenantUser", policy =>
    {
        policy.AuthenticationSchemes.Add(AuthSchemes.Tenant);
        policy.RequireClaim("TenantSlug");
    });

    // Tenant admin policy — requires TenantUser + Admin role
    options.AddPolicy("TenantAdmin", policy =>
    {
        policy.AuthenticationSchemes.Add(AuthSchemes.Tenant);
        policy.RequireClaim("TenantSlug");
        policy.RequireRole("Admin");
    });
});
```

---

## 3. Magic Link Token System

### Token Lifecycle

```
1. User submits email on login form
2. Server generates 32 random bytes → Base64Url encode → URL token
3. Server SHA256-hashes the token → stores hash in MagicLinkTokens table
4. Server sends email with: {baseUrl}/magic-link/{urlToken}
   (or {baseUrl}/{tenant}/magic-link/{urlToken} for tenant login)
5. User clicks link
6. Server SHA256-hashes the URL token → looks up hash in DB
7. If valid (not expired, not used) → mark as used → issue cookie
8. If invalid → show "link expired" page with option to request new one
```

### Why Hash Before Storing?

If the database is compromised, attackers cannot reconstruct valid magic link URLs from the stored hashes. Same principle as password hashing, but simpler since tokens are already high-entropy.

### MagicLinkService

```csharp
public class MagicLinkService
{
    private readonly CoreDbContext _coreDb;
    private readonly IEmailService _emailService;
    private readonly ILogger<MagicLinkService> _logger;
    private readonly MagicLinkOptions _options;

    /// <summary>
    /// Generate and send a magic link for the given email.
    /// Returns false if the email is not found in the relevant user store.
    /// </summary>
    public async Task<bool> SendMagicLinkAsync(
        string email,
        string? tenantSlug = null,
        CancellationToken ct = default)
    {
        // 1. Verify the email exists (super admin or tenant user)
        if (tenantSlug is null)
        {
            var superAdmin = await _coreDb.SuperAdmins
                .AnyAsync(sa => sa.Email == email && sa.IsActive, ct);
            if (!superAdmin) return false;
        }
        // For tenant users, verification happens in the caller (TenantAuthController)
        // because it needs TenantDbContext which is request-scoped

        // 2. Generate token
        var rawToken = GenerateToken();
        var hashedToken = HashToken(rawToken);

        // 3. Store hashed token
        var magicLink = new MagicLinkToken
        {
            Token = hashedToken,
            Email = email,
            TenantSlug = tenantSlug,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_options.TokenLifetimeMinutes),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };
        _coreDb.MagicLinkTokens.Add(magicLink);
        await _coreDb.SaveChangesAsync(ct);

        // 4. Build magic link URL
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var path = tenantSlug is not null
            ? $"/{tenantSlug}/magic-link/{rawToken}"
            : $"/magic-link/{rawToken}";
        var magicLinkUrl = $"{baseUrl}{path}";

        // 5. Send (email in production, console in development)
        await _emailService.SendMagicLinkAsync(email, magicLinkUrl);

        return true;
    }

    /// <summary>
    /// Verify a magic link token. Returns the associated email if valid.
    /// </summary>
    public async Task<MagicLinkVerifyResult> VerifyAsync(
        string rawToken,
        string? expectedTenantSlug = null,
        CancellationToken ct = default)
    {
        var hashedToken = HashToken(rawToken);

        var token = await _coreDb.MagicLinkTokens
            .FirstOrDefaultAsync(t =>
                t.Token == hashedToken
                && !t.IsUsed
                && t.ExpiresAt > DateTime.UtcNow
                && t.TenantSlug == expectedTenantSlug,
                ct);

        if (token is null)
            return MagicLinkVerifyResult.Invalid();

        // Mark as used
        token.IsUsed = true;
        token.UsedAt = DateTime.UtcNow;
        await _coreDb.SaveChangesAsync(ct);

        return MagicLinkVerifyResult.Valid(token.Email);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    private static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

public record MagicLinkVerifyResult(bool IsValid, string? Email)
{
    public static MagicLinkVerifyResult Valid(string email) => new(true, email);
    public static MagicLinkVerifyResult Invalid() => new(false, null);
}
```

### Configuration

```json
{
  "Auth": {
    "MagicLink": {
      "DeliveryMode": "Console",
      "TokenLifetimeMinutes": 15,
      "BaseUrl": "https://localhost:5001"
    },
    "SuperAdmin": {
      "Email": "admin@localhost"
    }
  }
}
```

| Setting | Local Default | Production |
|---------|---------------|------------|
| `DeliveryMode` | `Console` (logs link to terminal) | `Email` (sends via SES) |
| `TokenLifetimeMinutes` | `15` | `15` |
| `BaseUrl` | `https://localhost:5001` | `https://myapp.com` |
| `SuperAdmin:Email` | `admin@localhost` | `admin@myapp.com` |

### Token Cleanup

Expired and used tokens should be periodically purged:

```csharp
public class MagicLinkCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            using var scope = _serviceScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

            // Delete tokens older than 24 hours (regardless of used status)
            var cutoff = DateTime.UtcNow.AddHours(-24);
            await db.MagicLinkTokens
                .Where(t => t.CreatedAt < cutoff)
                .ExecuteDeleteAsync(stoppingToken);
        }
    }
}
```

---

## 4. Super Admin Authentication

Super admins are stored in `core.db` — they are NOT ASP.NET Identity users. They're simple records with an email address.

### SuperAdminAuthController

```csharp
public class SuperAdminAuthController : SwapController
{
    private readonly MagicLinkService _magicLinkService;
    private readonly IBotProtection _botProtection;

    // GET /login
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true
            && User.HasClaim("IsSuperAdmin", "true"))
        {
            return Redirect("/super-admin");
        }
        return SwapView();
    }

    // POST /login
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMagicLink(string email, string? turnstileToken)
    {
        // 1. Validate bot protection
        if (!await _botProtection.ValidateAsync(turnstileToken))
            return SwapView("Login").WithToast("Verification failed", ToastType.Error);

        // 2. Send magic link (always show success — don't leak whether email exists)
        await _magicLinkService.SendMagicLinkAsync(email);

        return SwapView("MagicLinkSent");
    }

    // GET /magic-link/{token}
    public async Task<IActionResult> VerifyMagicLink(string token)
    {
        var result = await _magicLinkService.VerifyAsync(token);

        if (!result.IsValid)
            return SwapView("MagicLinkExpired");

        // Issue super admin cookie
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, result.Email!),
            new(ClaimTypes.Name, result.Email!),
            new("IsSuperAdmin", "true"),
        };

        var identity = new ClaimsIdentity(claims, AuthSchemes.SuperAdmin);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            AuthSchemes.SuperAdmin,
            principal,
            new AuthenticationProperties { IsPersistent = true }
        );

        return Redirect("/super-admin");
    }

    // POST /logout
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.SuperAdmin);
        return Redirect("/");
    }
}
```

### Super Admin Seeding (on startup)

```csharp
// In Program.cs, after building the app
using var scope = app.Services.CreateScope();
var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
var adminEmail = config["Auth:SuperAdmin:Email"] ?? "admin@localhost";

if (!await coreDb.SuperAdmins.AnyAsync())
{
    coreDb.SuperAdmins.Add(new SuperAdmin
    {
        Email = adminEmail,
        DisplayName = "Super Admin",
        IsActive = true
    });
    await coreDb.SaveChangesAsync();
}
```

---

## 5. Tenant User Authentication

Tenant users are full **ASP.NET Identity** users stored in the tenant's own SQLite database. Magic link is the primary (and default) authentication method.

### TenantAuthController

```csharp
public class TenantAuthController : SwapController
{
    private readonly MagicLinkService _magicLinkService;
    private readonly ITenantContext _tenantContext;
    private readonly TenantDbContext _tenantDb;
    private readonly UserManager<AppUser> _userManager;
    private readonly IBotProtection _botProtection;

    // GET /{tenant}/login
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true
            && User.HasClaim("TenantSlug", _tenantContext.Slug!))
        {
            return Redirect($"/{_tenantContext.Slug}");
        }
        return SwapView();
    }

    // POST /{tenant}/login
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMagicLink(string email, string? turnstileToken)
    {
        if (!await _botProtection.ValidateAsync(turnstileToken))
            return SwapView("Login").WithToast("Verification failed", ToastType.Error);

        // Verify user exists in this tenant's Identity DB
        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null && user.IsActive)
        {
            await _magicLinkService.SendMagicLinkAsync(email, _tenantContext.Slug);
        }

        // Always show success (don't leak user existence)
        return SwapView("MagicLinkSent");
    }

    // GET /{tenant}/magic-link/{token}
    public async Task<IActionResult> VerifyMagicLink(string token)
    {
        var result = await _magicLinkService.VerifyAsync(token, _tenantContext.Slug);

        if (!result.IsValid)
            return SwapView("MagicLinkExpired");

        var user = await _userManager.FindByEmailAsync(result.Email!);
        if (user is null || !user.IsActive)
            return SwapView("MagicLinkExpired");

        // Load roles and permissions for claims
        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await _tenantDb.RolePermissions
            .Where(rp => roles.Contains(rp.Role.Name!))
            .Select(rp => rp.Permission.Key)
            .Distinct()
            .ToListAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email!),
            new("TenantSlug", _tenantContext.Slug!),
            new("TenantId", _tenantContext.TenantId.ToString()!),
        };

        // Add role claims
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        // Add permission claims
        foreach (var permission in permissions)
            claims.Add(new Claim("Permission", permission));

        var identity = new ClaimsIdentity(claims, AuthSchemes.Tenant);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            AuthSchemes.Tenant,
            principal,
            new AuthenticationProperties { IsPersistent = true }
        );

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return Redirect($"/{_tenantContext.Slug}");
    }

    // POST /{tenant}/logout
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Tenant);
        return Redirect($"/{_tenantContext.Slug}/login");
    }
}
```

### Tenant Cookie Isolation

The tenant slug is embedded in the cookie claims. On each request, the `CurrentUserMiddleware` validates that the tenant slug in the cookie matches the resolved tenant from the URL:

```csharp
public class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext,
        ICurrentUser currentUser)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            // Super admin context
            if (user.HasClaim("IsSuperAdmin", "true"))
            {
                ((CurrentUser)currentUser).Set(
                    userId: null,
                    email: user.FindFirstValue(ClaimTypes.Email),
                    displayName: user.FindFirstValue(ClaimTypes.Name),
                    isAuthenticated: true,
                    isSuperAdmin: true,
                    roles: [],
                    permissions: []
                );
            }
            // Tenant user context — verify slug matches
            else if (tenantContext.IsTenantRequest)
            {
                var cookieTenantSlug = user.FindFirstValue("TenantSlug");

                if (cookieTenantSlug == tenantContext.Slug)
                {
                    var roles = user.FindAll(ClaimTypes.Role)
                        .Select(c => c.Value).ToList();
                    var permissions = user.FindAll("Permission")
                        .Select(c => c.Value).ToList();

                    ((CurrentUser)currentUser).Set(
                        userId: user.FindFirstValue(ClaimTypes.NameIdentifier),
                        email: user.FindFirstValue(ClaimTypes.Email),
                        displayName: user.FindFirstValue(ClaimTypes.Name),
                        isAuthenticated: true,
                        isSuperAdmin: false,
                        roles: roles,
                        permissions: permissions
                    );
                }
                else
                {
                    // Cookie is for a different tenant — treat as unauthenticated
                    // This prevents cross-tenant session leakage
                    ((CurrentUser)currentUser).SetAnonymous();
                }
            }
        }

        await _next(context);
    }
}
```

---

## 6. ICurrentUser Implementation

```csharp
// Shared/ICurrentUser.cs
public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
    bool HasAnyPermission(params string[] permissions);
    bool HasAllPermissions(params string[] permissions);
    bool IsInRole(string role);
}

// Modules/Auth/Services/CurrentUser.cs
public class CurrentUser : ICurrentUser
{
    public string? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    public void Set(string? userId, string? email, string? displayName,
        bool isAuthenticated, bool isSuperAdmin,
        IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        UserId = userId;
        Email = email;
        DisplayName = displayName;
        IsAuthenticated = isAuthenticated;
        IsSuperAdmin = isSuperAdmin;
        Roles = roles;
        Permissions = permissions;
    }

    public void SetAnonymous()
    {
        UserId = null;
        Email = null;
        DisplayName = null;
        IsAuthenticated = false;
        IsSuperAdmin = false;
        Roles = [];
        Permissions = [];
    }

    public bool HasPermission(string permission)
        => IsSuperAdmin || Permissions.Contains(permission);

    public bool HasAnyPermission(params string[] permissions)
        => IsSuperAdmin || permissions.Any(p => Permissions.Contains(p));

    public bool HasAllPermissions(params string[] permissions)
        => IsSuperAdmin || permissions.All(p => Permissions.Contains(p));

    public bool IsInRole(string role)
        => Roles.Contains(role);
}
```

---

## 7. RBAC — Role-Based Access Control

### The Permission Hierarchy

```
Feature Flags (plan-level)     ◄── "Is this feature available to this tenant?"
    │
    ▼ (if feature enabled)
Permissions (role-level)       ◄── "Does this user's role grant this action?"
    │
    ▼ (if permitted)
Action executes
```

**Feature flags take precedence** — if a feature is disabled for a tenant's plan, no role or permission can override it. Permissions only matter within the scope of enabled features.

### Permission Conventions

Permissions follow a `{module}.{action}` naming convention:

```
notes.read          # View notes
notes.create        # Create new notes
notes.edit          # Edit existing notes
notes.delete        # Delete notes
users.read          # View user list
users.create        # Invite new users
users.edit          # Edit user details / roles
users.delete        # Deactivate users
roles.read          # View roles
roles.create        # Create new roles
roles.edit          # Edit role permissions
roles.delete        # Delete roles
settings.read       # View settings
settings.edit       # Modify settings
```

### Default Roles

Every new tenant database is seeded with these system roles:

| Role | System Role | Default Permissions |
|------|:-----------:|---------------------|
| **Admin** | ✅ | ALL permissions |
| **Member** | ✅ | `*.read` + `*.create` (read and create in all modules) |

Tenant admins can create additional custom roles and assign any combination of permissions.

### HasPermissionAttribute — Controller/Action Level

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class HasPermissionAttribute : TypeFilterAttribute
{
    public HasPermissionAttribute(string permission)
        : base(typeof(HasPermissionFilter))
    {
        Arguments = [permission];
    }

    public HasPermissionAttribute(params string[] permissions)
        : base(typeof(HasPermissionFilter))
    {
        Arguments = [permissions];
    }
}

public class HasPermissionFilter : IAsyncAuthorizationFilter
{
    private readonly string[] _permissions;
    private readonly ICurrentUser _currentUser;

    public HasPermissionFilter(string[] permissions, ICurrentUser currentUser)
    {
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!_currentUser.IsAuthenticated)
        {
            context.Result = new ChallengeResult();
            return Task.CompletedTask;
        }

        // Super admins bypass permission checks
        if (_currentUser.IsSuperAdmin)
            return Task.CompletedTask;

        if (!_currentUser.HasAnyPermission(_permissions))
        {
            context.Result = new ForbidResult();
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
```

**Usage on controllers:**

```csharp
[FeatureGate("notes")]                    // 1. Feature must be enabled for tenant's plan
[Authorize(Policy = "TenantUser")]        // 2. User must be authenticated as tenant user
public class NotesController : SwapController
{
    [HasPermission(PermissionDefinitions.NotesRead)]    // 3. User's role must have this permission
    public async Task<IActionResult> Index() { ... }

    [HasPermission(PermissionDefinitions.NotesCreate)]
    public async Task<IActionResult> Create() { ... }

    [HasPermission(PermissionDefinitions.NotesDelete)]
    public async Task<IActionResult> Delete(Guid id) { ... }
}
```

---

## 8. Tag Helpers — View-Level Access Control

### HasPermission Tag Helper

Show/hide UI elements based on the current user's permissions:

```csharp
[HtmlTargetElement("has-permission", Attributes = "name")]
public class HasPermissionTagHelper : TagHelper
{
    private readonly ICurrentUser _currentUser;

    [HtmlAttributeName("name")]
    public string Permission { get; set; } = string.Empty;

    [HtmlAttributeName("any")]
    public string? AnyPermissions { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null; // Don't render the tag itself

        bool hasAccess;

        if (!string.IsNullOrEmpty(AnyPermissions))
        {
            var permissions = AnyPermissions.Split(',', StringSplitOptions.TrimEntries);
            hasAccess = _currentUser.HasAnyPermission(permissions);
        }
        else
        {
            hasAccess = _currentUser.HasPermission(Permission);
        }

        if (!hasAccess)
        {
            output.SuppressOutput();
        }
    }
}
```

**Usage in views:**

```html
<!-- Show only if user has notes.create permission -->
<has-permission name="notes.create">
    <button hx-get="/@tenantSlug/notes/create"
            hx-target="#modal-container"
            class="btn btn-primary">
        New Note
    </button>
</has-permission>

<!-- Show if user has ANY of these permissions -->
<has-permission any="users.edit, users.delete">
    <div class="dropdown">
        <!-- User action menu -->
    </div>
</has-permission>
```

### IsSuperAdmin Tag Helper

Show elements only for super admins (useful in shared layouts):

```csharp
[HtmlTargetElement("is-super-admin")]
public class IsSuperAdminTagHelper : TagHelper
{
    private readonly ICurrentUser _currentUser;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        if (!_currentUser.IsSuperAdmin)
            output.SuppressOutput();
    }
}
```

**Usage:**

```html
<is-super-admin>
    <a swap-nav href="/super-admin" class="menu-item">
        Super Admin Panel
    </a>
</is-super-admin>
```

### IsAuthenticated Tag Helper

```csharp
[HtmlTargetElement("is-authenticated")]
public class IsAuthenticatedTagHelper : TagHelper
{
    private readonly ICurrentUser _currentUser;

    [HtmlAttributeName("negate")]
    public bool Negate { get; set; } = false;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        var isAuth = _currentUser.IsAuthenticated;
        if (Negate ? isAuth : !isAuth)
            output.SuppressOutput();
    }
}
```

**Usage:**

```html
<!-- Show login button when NOT authenticated -->
<is-authenticated negate="true">
    <a href="/login" class="btn btn-ghost">Login</a>
</is-authenticated>

<!-- Show user menu when authenticated -->
<is-authenticated>
    <div class="dropdown dropdown-end">
        <span>@currentUser.DisplayName</span>
    </div>
</is-authenticated>
```

---

## 9. Tag Helper Registration

All auth tag helpers must be registered in `_ViewImports.cshtml`:

```html
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Swap.Htmx
@addTagHelper *, saas
```

The `@addTagHelper *, saas` line picks up all tag helpers in the application assembly — including `HasPermissionTagHelper`, `IsSuperAdminTagHelper`, `IsAuthenticatedTagHelper`, and the `FeatureTagHelper` (from [05 — Feature Flags](05-feature-flags.md)).

---

## 10. Session Security Considerations

### Cross-Tenant Protection

- Tenant slug is stored in the cookie claims
- `CurrentUserMiddleware` validates that cookie tenant slug matches URL tenant slug
- Mismatch = treated as unauthenticated (prevents session leakage between tenants)
- A user authenticated at `acme.myapp.com` cannot access `globex.myapp.com` with the same cookie

### Token Security

- Magic link tokens are 32 bytes of cryptographic randomness (256 bits of entropy)
- Tokens are SHA256-hashed before database storage
- Tokens are one-time use (marked as used on first verification)
- Tokens expire after 15 minutes (configurable)
- Expired/used tokens are cleaned up hourly

### Cookie Security

- `HttpOnly = true` — prevents JavaScript access
- `SameSite = Lax` — prevents CSRF in most cases
- `Secure = SameAsRequest` — HTTPS in production, HTTP allowed locally
- Sliding expiration — active users stay logged in
- Super admin: 24-hour expiry
- Tenant users: 12-hour expiry

### Anti-Forgery

All POST/PUT/DELETE forms include anti-forgery tokens (`[ValidateAntiForgeryToken]`). Swap.Htmx handles this automatically when using `hx-post` with proper configuration.

---

## 11. User Invitation Flow (Tenant Admin)

When a tenant admin invites a new user:

```
1. Admin fills invite form (email, role selection)
2. Server creates AppUser in TenantDbContext (EmailConfirmed = false)
3. Server assigns selected role
4. Server generates magic link token
5. Server sends invite email with magic link
6. New user clicks link → verified → EmailConfirmed = true → logged in
```

This leverages the same magic link infrastructure — no separate invite system needed.

---

## 12. Auth Module Files Summary

```
Modules/Auth/
├── README.md
├── AuthModule.cs
├── Controllers/
│   ├── SuperAdminAuthController.cs    # /login, /magic-link/{token}, /logout
│   └── TenantAuthController.cs        # /{tenant}/login, /{tenant}/magic-link/{token}
├── Services/
│   ├── MagicLinkService.cs            # Token generation, verification, delivery
│   ├── MagicLinkCleanupService.cs     # Background cleanup of expired tokens
│   ├── CurrentUser.cs                 # ICurrentUser implementation
│   └── AuthCookieService.cs           # Cookie issuance helpers (if needed)
├── Middleware/
│   └── CurrentUserMiddleware.cs       # Populates ICurrentUser per request
├── Filters/
│   ├── HasPermissionAttribute.cs      # [HasPermission("notes.create")]
│   └── HasPermissionFilter.cs         # IAsyncAuthorizationFilter
├── TagHelpers/
│   ├── HasPermissionTagHelper.cs      # <has-permission name="...">
│   ├── IsSuperAdminTagHelper.cs       # <is-super-admin>
│   └── IsAuthenticatedTagHelper.cs    # <is-authenticated>
├── Views/
│   ├── _ViewImports.cshtml
│   ├── SuperAdminLogin.cshtml         # Email input + Turnstile
│   ├── TenantLogin.cshtml             # Email input + Turnstile (tenant branded)
│   ├── MagicLinkSent.cshtml           # "Check your email" page
│   └── MagicLinkExpired.cshtml        # "Link expired" + request new
└── Events/
    ├── AuthEventConfig.cs
    └── AuthEvents.cs
```

---

## Next Steps

→ [05 — Feature Flags](05-feature-flags.md) for plan-linked feature management, database-backed feature definitions, and the `<feature>` tag helper.
