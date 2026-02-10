using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.FeatureFlags.Entities;
using saas.Modules.SuperAdmin.Entities;
using saas.Shared;

namespace saas.Data.Seeding;

/// <summary>
/// Seeds core database with plans, features, plan-feature mappings, and default super admin.
/// Features are collected from IModule.Features across all registered modules.
/// Idempotent — skips if data already exists.
/// </summary>
public static class MasterDataSeeder
{
    public static async Task SeedAsync(CoreDbContext db, IConfiguration configuration, IReadOnlyList<IModule> modules)
    {
        if (await db.Plans.AnyAsync())
            return;

        // 1. Seed Plans
        var freePlan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Free",
            Slug = "free",
            Description = "Get started for free",
            MonthlyPrice = 0,
            AnnualPrice = null,
            Currency = "ZAR",
            SortOrder = 0,
            MaxUsers = 3,
            IsActive = true
        };

        var starterPlan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Starter",
            Slug = "starter",
            Description = "For small teams getting started",
            MonthlyPrice = 199,
            AnnualPrice = 1990,
            Currency = "ZAR",
            SortOrder = 1,
            MaxUsers = 10,
            IsActive = true
        };

        var professionalPlan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Professional",
            Slug = "professional",
            Description = "For growing teams",
            MonthlyPrice = 499,
            AnnualPrice = 4990,
            Currency = "ZAR",
            SortOrder = 2,
            MaxUsers = 25,
            IsActive = true
        };

        var enterprisePlan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Enterprise",
            Slug = "enterprise",
            Description = "For large organisations",
            MonthlyPrice = 999,
            AnnualPrice = 9990,
            Currency = "ZAR",
            SortOrder = 3,
            MaxUsers = null, // unlimited
            IsActive = true
        };

        db.Plans.AddRange(freePlan, starterPlan, professionalPlan, enterprisePlan);

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

        // 3. Seed Plan-Feature mappings (matrix from doc 05 §10)
        var featureLookup = features.ToDictionary(f => f.Key);

        // Starter gets: Notes, Projects, CustomRoles, ExportData
        var starterFeatures = new[] { "notes", "projects", "custom_roles", "export_data" };
        foreach (var key in starterFeatures)
        {
            if (featureLookup.TryGetValue(key, out var feature))
                db.PlanFeatures.Add(new PlanFeature { PlanId = starterPlan.Id, FeatureId = feature.Id });
        }

        // Professional gets: everything Starter has + AdvancedReports, AuditLog
        var professionalFeatures = new[] { "notes", "projects", "custom_roles", "export_data", "advanced_reports", "audit_log" };
        foreach (var key in professionalFeatures)
        {
            if (featureLookup.TryGetValue(key, out var feature))
                db.PlanFeatures.Add(new PlanFeature { PlanId = professionalPlan.Id, FeatureId = feature.Id });
        }

        // Enterprise gets: ALL features
        foreach (var feature in features)
        {
            db.PlanFeatures.Add(new PlanFeature { PlanId = enterprisePlan.Id, FeatureId = feature.Id });
        }

        // Free plan gets NO features

        await db.SaveChangesAsync();

        // 4. Seed default Super Admin
        var adminEmail = configuration.GetValue<string>("SuperAdmin:Email") ?? "admin@localhost";
        db.SuperAdmins.Add(new SuperAdmin
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            DisplayName = "Super Admin",
            IsActive = true
        });

        await db.SaveChangesAsync();
    }
}
