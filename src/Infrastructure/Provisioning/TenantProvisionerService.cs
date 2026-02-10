using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;
using saas.Modules.Billing.Entities;
using saas.Modules.Tenancy.Entities;
using saas.Shared;

namespace saas.Infrastructure.Provisioning;

public partial class TenantProvisionerService : ITenantProvisioner
{
    private readonly CoreDbContext _coreDb;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantProvisionerService> _logger;
    private readonly IReadOnlyList<IModule> _modules;
    private readonly HashSet<string> _reservedSlugs;

    public TenantProvisionerService(
        CoreDbContext coreDb,
        IServiceProvider serviceProvider,
        ILogger<TenantProvisionerService> logger,
        IReadOnlyList<IModule> modules)
    {
        _coreDb = coreDb;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _modules = modules;

        // Collect reserved slugs from all modules (explicit + public route prefixes)
        _reservedSlugs = new HashSet<string>(
            modules.SelectMany(m => m.ReservedSlugs)
                .Concat(modules.SelectMany(m => m.PublicRoutePrefixes))
                .Where(s => !string.IsNullOrEmpty(s)),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<TenantProvisioningResult> ProvisionTenantAsync(
        string slug, 
        string adminEmail, 
        Guid planId)
    {
        // Check if tenant already exists (paid-plan flow creates tenant in PendingSetup first)
        var existingTenant = await _coreDb.Tenants.FirstOrDefaultAsync(t => t.Slug == slug.ToLowerInvariant());

        // Only validate slug uniqueness when there is no existing tenant
        if (existingTenant is null)
        {
            var slugValidation = await ValidateSlugAsync(slug);
            if (!slugValidation.IsValid)
            {
                return new TenantProvisioningResult(false, ErrorMessage: slugValidation.ErrorMessage);
            }
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
            Tenant tenant;

            if (existingTenant is not null)
            {
                // Tenant already exists (created during paid registration) — just activate it
                tenant = existingTenant;
                tenant.Status = TenantStatus.Active;
                tenant.UpdatedAt = DateTime.UtcNow;
                await _coreDb.SaveChangesAsync();

                _logger.LogInformation("Activating existing tenant: {TenantId} ({Slug})", tenant.Id, tenant.Slug);
            }
            else
            {
                // Create new tenant record (free plan flow)
                tenant = new Tenant
                {
                    Id = Guid.NewGuid(),
                    Slug = slug.ToLowerInvariant(),
                    Name = slug,
                    ContactEmail = adminEmail,
                    PlanId = planId,
                    Status = TenantStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _coreDb.Tenants.Add(tenant);
                await _coreDb.SaveChangesAsync();

                _logger.LogInformation("Created tenant record: {TenantId} ({Slug})", tenant.Id, tenant.Slug);
            }

            // Create subscription only if one doesn't already exist for this tenant
            var hasSubscription = await _coreDb.Subscriptions.AnyAsync(s => s.TenantId == tenant.Id);
            if (!hasSubscription)
            {
                var subscription = new Subscription
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    PlanId = planId,
                    Status = SubscriptionStatus.Trialing,
                    BillingCycle = BillingCycle.Monthly,
                    StartDate = DateTime.UtcNow,
                    NextBillingDate = DateTime.UtcNow.AddDays(14),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _coreDb.Subscriptions.Add(subscription);
                await _coreDb.SaveChangesAsync();

                _logger.LogInformation("Created subscription: {SubscriptionId} for tenant {TenantId}",
                    subscription.Id, tenant.Id);
            }

            // Create tenant database
            var dbPath = Path.Combine("db", "tenants", $"{tenant.Slug}.db");
            var dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            // Create a scoped TenantDbContext for the new tenant
            var connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
            var walInterceptor = new WalModeInterceptor();
            var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
            optionsBuilder.UseSqlite(connectionString)
                          .AddInterceptors(walInterceptor);

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

            // Create roles — collected from all modules, deduplicated by name
            var roleDefinitions = _modules
                .SelectMany(m => m.DefaultRoles)
                .DistinctBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var createdRoles = new Dictionary<string, AppRole>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleDef in roleDefinitions)
            {
                if (!await roleManager.RoleExistsAsync(roleDef.Name))
                {
                    var role = new AppRole
                    {
                        Name = roleDef.Name,
                        NormalizedName = roleDef.Name.ToUpperInvariant(),
                        Description = roleDef.Description,
                        IsSystemRole = roleDef.IsSystemRole
                    };
                    await roleManager.CreateAsync(role);
                    createdRoles[roleDef.Name] = role;
                }
                else
                {
                    var role = await roleManager.FindByNameAsync(roleDef.Name);
                    if (role is not null) createdRoles[roleDef.Name] = role;
                }
            }

            // Seed permissions — collected from all modules
            var modulePermissions = _modules
                .SelectMany(m => m.Permissions)
                .ToList();

            var permissions = modulePermissions.Select(mp => new Permission
            {
                Id = Guid.NewGuid(),
                Key = mp.Key,
                Name = mp.Name,
                Group = mp.Group,
                SortOrder = mp.SortOrder,
                Description = mp.Description
            }).ToList();

            tenantDb.Permissions.AddRange(permissions);
            await tenantDb.SaveChangesAsync();

            // Grant all permissions to Admin role
            if (createdRoles.TryGetValue("Admin", out var adminRole))
            {
                var adminRolePermissions = permissions.Select(p => new RolePermission
                {
                    RoleId = adminRole.Id,
                    PermissionId = p.Id
                });
                tenantDb.RolePermissions.AddRange(adminRolePermissions);
                await tenantDb.SaveChangesAsync();
            }

            // Grant module-defined permissions to non-admin roles
            var permissionLookup = permissions.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
            var roleMappings = _modules
                .SelectMany(m => m.DefaultRolePermissions)
                .ToList();

            foreach (var mapping in roleMappings)
            {
                if (createdRoles.TryGetValue(mapping.RoleName, out var role) &&
                    permissionLookup.TryGetValue(mapping.PermissionKey, out var perm))
                {
                    tenantDb.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = perm.Id
                    });
                }
            }
            await tenantDb.SaveChangesAsync();

            _logger.LogInformation("Seeded {Count} permissions and {RoleCount} roles for tenant {Slug}",
                permissions.Count, createdRoles.Count, tenant.Slug);

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
        if (_reservedSlugs.Contains(slug))
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
