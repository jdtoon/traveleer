using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using saas.Modules.Billing.Entities;
using saas.Modules.FeatureFlags.Entities;
using saas.Modules.SuperAdmin.Entities;
using saas.Shared;

namespace saas.Data.Core;

/// <summary>
/// Seeds core database with plans, features, plan-feature mappings, and default super admin.
/// Features and their plan tier assignments are collected entirely from IModule.Features.
/// Idempotent — skips if data already exists.
/// </summary>
public static class CoreDataSeeder
{
    public static async Task SeedAsync(CoreDbContext db, IConfiguration configuration, IReadOnlyList<IModule> modules, IPasswordHasher<SuperAdmin>? passwordHasher = null)
    {
        if (await db.Plans.AnyAsync())
        {
            // Plans exist — run incremental sync for new features/permissions
            await SyncNewFeaturesAsync(db, modules);
            return;
        }

        // 1. Seed Plans
        var plans = new[]
        {
            new Plan
            {
                Id = Guid.NewGuid(), Name = "Free", Slug = "free",
                Description = "Get started for free",
                MonthlyPrice = 0, AnnualPrice = null, Currency = "ZAR",
                BillingModel = BillingModel.FlatRate,
                SortOrder = 0, MaxUsers = 3, MaxRequestsPerMinute = 30, IsActive = true
            },
            new Plan
            {
                Id = Guid.NewGuid(), Name = "Starter", Slug = "starter",
                Description = "For small teams getting started",
                MonthlyPrice = 199, AnnualPrice = 1990, Currency = "ZAR",
                BillingModel = BillingModel.FlatRate,
                SortOrder = 1, MaxUsers = 10, MaxRequestsPerMinute = 60, IsActive = true
            },
            new Plan
            {
                Id = Guid.NewGuid(), Name = "Professional", Slug = "professional",
                Description = "For growing teams",
                MonthlyPrice = 499, AnnualPrice = 4990, Currency = "ZAR",
                BillingModel = BillingModel.FlatRate,
                SortOrder = 2, MaxUsers = 25, MaxRequestsPerMinute = 120, IsActive = true
            },
            new Plan
            {
                Id = Guid.NewGuid(), Name = "Enterprise", Slug = "enterprise",
                Description = "For large organisations",
                MonthlyPrice = 999, AnnualPrice = 9990, Currency = "ZAR",
                BillingModel = BillingModel.FlatRate,
                SortOrder = 3, MaxUsers = null, MaxRequestsPerMinute = null, IsActive = true
            }
        };

        db.Plans.AddRange(plans);

        // 2. Seed Features — collected from all modules
        var features = modules
            .SelectMany(m => m.Features.Select(mf => new Feature
            {
                Id = Guid.NewGuid(),
                Key = mf.Key,
                Name = mf.Name,
                Module = m.Name,
                Description = mf.Description,
                IsGlobal = mf.IsGlobal,
                IsEnabled = true
            }))
            .ToList();
        db.Features.AddRange(features);

        await db.SaveChangesAsync();

        // 3. Seed Plan-Feature mappings — driven by ModuleFeature.MinPlanSlug
        // Build a lookup: feature key → MinPlanSlug from the module declaration
        var featureMinPlan = modules
            .SelectMany(m => m.Features)
            .ToDictionary(mf => mf.Key, mf => mf.MinPlanSlug, StringComparer.OrdinalIgnoreCase);

        // Build plan slug → SortOrder lookup
        var planSortOrder = plans.ToDictionary(p => p.Slug, p => p.SortOrder, StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            foreach (var feature in features)
            {
                // Get the minimum plan slug for this feature
                if (!featureMinPlan.TryGetValue(feature.Key, out var minSlug))
                    continue;

                // MinPlanSlug = null means available on ALL plans (including Free)
                if (minSlug is null)
                {
                    db.PlanFeatures.Add(new PlanFeature { PlanId = plan.Id, FeatureId = feature.Id });
                    continue;
                }

                // If the current plan's SortOrder >= the minimum plan's SortOrder, assign the feature
                if (planSortOrder.TryGetValue(minSlug, out var minSortOrder) && plan.SortOrder >= minSortOrder)
                {
                    db.PlanFeatures.Add(new PlanFeature { PlanId = plan.Id, FeatureId = feature.Id });
                }
            }
        }

        await db.SaveChangesAsync();

        // 4. Seed default Super Admin
        var adminEmail = configuration.GetValue<string>("SuperAdmin:Email") ?? "admin@localhost";
        var adminPassword = configuration.GetValue<string>("SuperAdmin:Password");

        var superAdmin = new SuperAdmin
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            DisplayName = "Super Admin",
            IsActive = true
        };

        if (passwordHasher is not null && !string.IsNullOrWhiteSpace(adminPassword))
            superAdmin.PasswordHash = passwordHasher.HashPassword(superAdmin, adminPassword);

        db.SuperAdmins.Add(superAdmin);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Incrementally syncs new features from IModule definitions into the database.
    /// Runs on every startup after the initial seed to pick up newly added module features.
    /// </summary>
    private static async Task SyncNewFeaturesAsync(CoreDbContext db, IReadOnlyList<IModule> modules)
    {
        var existingFeatureKeys = await db.Features.Select(f => f.Key).ToListAsync();
        var existingKeySet = new HashSet<string>(existingFeatureKeys, StringComparer.OrdinalIgnoreCase);

        var moduleFeatures = modules.SelectMany(m => m.Features.Select(mf => new { Module = m, Feature = mf })).ToList();
        var newModuleFeatures = moduleFeatures.Where(x => !existingKeySet.Contains(x.Feature.Key)).ToList();

        if (newModuleFeatures.Count == 0)
            return;

        // Add new features
        var newFeatures = newModuleFeatures.Select(x => new Feature
        {
            Id = Guid.NewGuid(),
            Key = x.Feature.Key,
            Name = x.Feature.Name,
            Module = x.Module.Name,
            Description = x.Feature.Description,
            IsGlobal = x.Feature.IsGlobal,
            IsEnabled = true
        }).ToList();

        db.Features.AddRange(newFeatures);
        await db.SaveChangesAsync();
    }
}
