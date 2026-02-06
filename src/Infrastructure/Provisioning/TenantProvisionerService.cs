using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Core;
using saas.Data.Tenant;

namespace saas.Infrastructure.Provisioning;

public partial class TenantProvisionerService : ITenantProvisioner
{
    private readonly CoreDbContext _coreDb;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantProvisionerService> _logger;
    
    // Reserved slugs that cannot be used for tenant registration
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "super-admin", "admin", "api", "health", "pricing", "about", 
        "contact", "register", "login", "logout", "www", "app", "cdn",
        "static", "assets", "docs", "help", "support", "blog", "status"
    };

    public TenantProvisionerService(
        CoreDbContext coreDb,
        IServiceProvider serviceProvider,
        ILogger<TenantProvisionerService> logger)
    {
        _coreDb = coreDb;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<TenantProvisioningResult> ProvisionTenantAsync(
        string slug, 
        string adminEmail, 
        Guid planId)
    {
        // Validate slug
        var slugValidation = await ValidateSlugAsync(slug);
        if (!slugValidation.IsValid)
        {
            return new TenantProvisioningResult(false, ErrorMessage: slugValidation.ErrorMessage);
        }

        // Validate plan exists
        var plan = await _coreDb.Plans.FindAsync(planId);
        if (plan == null)
        {
            return new TenantProvisioningResult(false, ErrorMessage: "Invalid plan selected");
        }

        // Validate email format
        if (string.IsNullOrWhiteSpace(adminEmail) || !IsValidEmail(adminEmail))
        {
            return new TenantProvisioningResult(false, ErrorMessage: "Invalid email address");
        }

        try
        {
            // Create tenant record in Core database
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Slug = slug.ToLowerInvariant(),
                Name = slug, // Can be updated later by admin
                PlanId = planId,
                Status = TenantStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _coreDb.Tenants.Add(tenant);
            await _coreDb.SaveChangesAsync();

            _logger.LogInformation("Created tenant record: {TenantId} ({Slug})", tenant.Id, tenant.Slug);

            // Create subscription
            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                PlanId = planId,
                Status = SubscriptionStatus.Trialing,
                BillingCycle = BillingCycle.Monthly,
                StartDate = DateTime.UtcNow,
                NextBillingDate = DateTime.UtcNow.AddDays(14), // 14-day trial
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _coreDb.Subscriptions.Add(subscription);
            await _coreDb.SaveChangesAsync();

            _logger.LogInformation("Created subscription: {SubscriptionId} for tenant {TenantId}", 
                subscription.Id, tenant.Id);

            // Create tenant database
            var dbPath = Path.Combine("db", "tenants", $"{tenant.Slug}.db");
            var dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            // Create a scoped TenantDbContext for the new tenant
            var connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
            var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
            optionsBuilder.UseSqlite(connectionString);

            using var tenantDb = new TenantDbContext(optionsBuilder.Options);
            
            // Apply migrations to create schema
            await tenantDb.Database.MigrateAsync();
            _logger.LogInformation("Applied migrations to tenant database: {DbPath}", dbPath);

            // Create initial admin user
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();

            // Temporarily set the database for this scope
            var scopedTenantDb = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            scopedTenantDb.Database.SetConnectionString(connectionString);

            // Create roles
            var adminRole = new AppRole { Name = "Admin", NormalizedName = "ADMIN", IsSystemRole = true };
            var userRole = new AppRole { Name = "User", NormalizedName = "USER", IsSystemRole = true };

            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(adminRole);
            }
            else
            {
                adminRole = await roleManager.FindByNameAsync("Admin") ?? adminRole;
            }
            
            if (!await roleManager.RoleExistsAsync("User"))
            {
                await roleManager.CreateAsync(userRole);
            }

            // Create admin user
            var adminUser = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true, // Auto-confirm for initial admin
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(adminUser);
            if (!result.Succeeded)
            {
                _logger.LogError("Failed to create admin user: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Description)));
                
                // Clean up - delete tenant record
                _coreDb.Tenants.Remove(tenant);
                await _coreDb.SaveChangesAsync();
                
                return new TenantProvisioningResult(false, 
                    ErrorMessage: $"Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }

            // Assign admin role
            await userManager.AddToRoleAsync(adminUser, "Admin");

            _logger.LogInformation("Created admin user {Email} for tenant {TenantId}", adminEmail, tenant.Id);

            return new TenantProvisioningResult(true, TenantId: tenant.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error provisioning tenant {Slug}", slug);
            return new TenantProvisioningResult(false, ErrorMessage: "An error occurred during provisioning");
        }
    }

    public async Task<SlugValidationResult> ValidateSlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return new SlugValidationResult(false, "Slug is required");
        }

        slug = slug.ToLowerInvariant().Trim();

        // Length validation
        if (slug.Length < 3)
        {
            return new SlugValidationResult(false, "Slug must be at least 3 characters");
        }

        if (slug.Length > 63)
        {
            return new SlugValidationResult(false, "Slug must not exceed 63 characters");
        }

        // Format validation (alphanumeric and hyphens only, must start/end with alphanumeric)
        if (!SlugRegex().IsMatch(slug))
        {
            return new SlugValidationResult(false, 
                "Slug must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number");
        }

        // Reserved slug check
        if (ReservedSlugs.Contains(slug))
        {
            return new SlugValidationResult(false, "This slug is reserved and cannot be used");
        }

        // Uniqueness check
        var exists = await _coreDb.Tenants.AnyAsync(t => t.Slug == slug);
        if (exists)
        {
            return new SlugValidationResult(false, "This slug is already taken");
        }

        return new SlugValidationResult(true);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*[a-z0-9]$")]
    private static partial Regex SlugRegex();

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
