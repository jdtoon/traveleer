using System.Globalization;
using saas.Infrastructure;
using saas.Infrastructure.Messaging;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Infrastructure.HealthChecks;
using saas.Modules.Tenancy.Services;
using saas.Shared;

// Force InvariantCulture for SQLite decimal collation compatibility
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// STRUCTURED LOGGING (Serilog)
// =============================================================================

builder.AddSerilogConfig();

// =============================================================================
// INFRASTRUCTURE SERVICES
// =============================================================================

builder.Services.AddDataProtectionConfig(builder.Environment);
builder.Services.AddCompressionConfig();
builder.Services.AddForwardedHeadersConfig();
builder.Services.AddRateLimitingConfig(builder.Configuration);

// =============================================================================
// MESSAGING (MassTransit)
// =============================================================================

builder.Services.AddMessagingConfig(builder.Configuration);

// =============================================================================
// CACHING
// =============================================================================

builder.Services.AddCachingConfig(builder.Configuration);

// =============================================================================
// JOB SCHEDULING (Hangfire)
// =============================================================================

builder.Services.AddSchedulingConfig(builder.Configuration);

// =============================================================================
// DATABASE & CORE SERVICES
// =============================================================================

builder.Services.AddDatabaseConfig(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CoreDbContext>("core-database")
    .AddCheck<TenantDirectoryHealthCheck>("tenant-directory")
    .AddCheck<BackupReadinessHealthCheck>("backup-readiness");
builder.Services.AddCoreServices(builder.Configuration);
builder.Services.AddSingleton<saas.Infrastructure.Services.IEmailTemplateService, saas.Infrastructure.Services.EmailTemplateService>();

// =============================================================================
// DOMAIN MODULES
// =============================================================================

var modules = new IModule[]
{
    new saas.Modules.Tenancy.TenancyModule(),
    new saas.Modules.Billing.BillingModule(),
    new saas.Modules.Marketing.MarketingModule(),
    new saas.Modules.Auth.AuthModule(),
    new saas.Modules.SuperAdmin.SuperAdminModule(),
    new saas.Modules.Registration.RegistrationModule(),
    new saas.Modules.FeatureFlags.FeatureFlagsModule(),
    new saas.Modules.Dashboard.DashboardModule(),
    new saas.Modules.Notes.NotesModule(),
    new saas.Modules.Audit.AuditModule(),
    new saas.Modules.TenantAdmin.TenantAdminModule(),
    new saas.Modules.Backup.BackupModule(),
    new saas.Modules.Notifications.NotificationsModule()
};

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
    builder.Logging.AddFilter("saas", LogLevel.Information);
}

// Register module list in DI for seeder/provisioner access
builder.Services.AddSingleton<IReadOnlyList<IModule>>(modules);

// Collect view paths from all modules for the Razor view locator
var controllerViewPaths = modules
    .SelectMany(m => m.ControllerViewPaths)
    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

// Collect partial view search paths from modules for Swap.Htmx
var partialViewSearchPaths = modules
    .SelectMany(m => m.PartialViewSearchPaths)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

// Collect public route prefixes from modules for TenantResolutionMiddleware
var publicRoutePrefixes = modules
    .SelectMany(m => m.PublicRoutePrefixes)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

// System-level reserved prefixes — never treated as tenant slugs
foreach (var reserved in new[] { "api", "swagger", ".well-known", "assets", "static", "webhook", "hangfire", "health", "favicon.svg", "manifest.json", "css", "js", "lib", "errors" })
    publicRoutePrefixes.Add(reserved);

// =============================================================================
// MVC & WEB
// =============================================================================

builder.Services.AddWebOptimizerConfig(builder.Environment);
builder.Services.AddMvcConfig(controllerViewPaths);
builder.Services.AddSwapHtmxConfig(partialViewSearchPaths);
builder.Services.AddStorageConfig(builder.Configuration);

// Register public route prefixes for TenantResolutionMiddleware
builder.Services.AddSingleton(publicRoutePrefixes);

// Background: clean up abandoned PendingSetup tenants
builder.Services.AddHostedService<PendingTenantCleanupService>();

// =============================================================================
// BUILD & RUN
// =============================================================================

var app = builder.Build();

// Initialize error page cache from wwwroot/errors/ HTML files
ErrorPages.Initialize(app.Environment);

await app.RestoreFromBackupIfNeededAsync();
await app.InitializeDatabaseAsync();

foreach (var module in modules)
    module.RegisterMiddleware(app);

app.ConfigurePipeline();
app.MapEndpoints();
app.UseSchedulingDashboard();
app.RegisterRecurringJobs();

app.Run();

// Make Program class accessible to integration tests
public partial class Program { }
