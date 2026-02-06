using saas.Infrastructure;
using saas.Data.Core;
using saas.Data.Audit;
using saas.Data.Seeding;
using saas.Infrastructure.HealthChecks;
using saas.Shared;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// INFRASTRUCTURE SERVICES
// =============================================================================

builder.Services.AddDataProtectionConfig(builder.Environment);
builder.Services.AddCompressionConfig();
builder.Services.AddForwardedHeadersConfig();
builder.Services.AddRateLimitingConfig();

// =============================================================================
// DATABASE & CORE SERVICES
// =============================================================================

builder.Services.AddDatabaseConfig(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CoreDbContext>("core-database")
    .AddCheck<TenantDirectoryHealthCheck>("tenant-directory");
builder.Services.AddCoreServices();

// =============================================================================
// DOMAIN MODULES
// =============================================================================

var modules = new IModule[]
{
    new saas.Modules.Auth.AuthModule(),
    new saas.Modules.SuperAdmin.SuperAdminModule()
};

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
    builder.Logging.AddFilter("saas", LogLevel.Information);
}

// =============================================================================
// MVC & WEB
// =============================================================================

builder.Services.AddWebOptimizerConfig(builder.Environment);
builder.Services.AddMvcConfig();
builder.Services.AddSwapHtmxConfig();

// =============================================================================
// BUILD & RUN
// =============================================================================

var app = builder.Build();

await app.InitializeDatabaseAsync();

foreach (var module in modules)
    module.RegisterMiddleware(app);

app.ConfigurePipeline();
app.MapEndpoints();

app.Run();

// Make Program class accessible to integration tests
public partial class Program { }
