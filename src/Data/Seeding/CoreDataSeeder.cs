using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Shared;

namespace saas.Data.Seeding;

/// <summary>
/// Seeds core database with plans, features, plan-feature mappings, and default super admin.
/// Idempotent — skips if data already exists.
/// </summary>
public static class MasterDataSeeder
{
    public static async Task SeedAsync(CoreDbContext db, IConfiguration configuration)
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

        // 2. Seed Features
        var features = FeatureDefinitions.GetAll();
        db.Features.AddRange(features);

        await db.SaveChangesAsync();

        // 3. Seed Plan-Feature mappings (matrix from doc 05 §10)
        var featureLookup = features.ToDictionary(f => f.Key);

        // Starter gets: Notes, Projects, CustomRoles, ExportData
        var starterFeatures = new[] { FeatureDefinitions.Notes, FeatureDefinitions.Projects, FeatureDefinitions.CustomRoles, FeatureDefinitions.ExportData };
        foreach (var key in starterFeatures)
        {
            if (featureLookup.TryGetValue(key, out var feature))
                db.PlanFeatures.Add(new PlanFeature { PlanId = starterPlan.Id, FeatureId = feature.Id });
        }

        // Professional gets: everything Starter has + AdvancedReports, AuditLog
        var professionalFeatures = new[] { FeatureDefinitions.Notes, FeatureDefinitions.Projects, FeatureDefinitions.CustomRoles, FeatureDefinitions.ExportData, FeatureDefinitions.AdvancedReports, FeatureDefinitions.AuditLog };
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
