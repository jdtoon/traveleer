using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.FeatureFlags.Entities;

namespace saas.Modules.FeatureFlags.Data;

public class FeatureConfiguration : IEntityTypeConfiguration<Feature>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Feature> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Key).IsUnique();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Module).HasMaxLength(100);
    }
}

public class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.HasKey(e => new { e.PlanId, e.FeatureId });
        builder.HasOne(e => e.Plan).WithMany(p => p.PlanFeatures).HasForeignKey(e => e.PlanId);
        builder.HasOne(e => e.Feature).WithMany(f => f.PlanFeatures).HasForeignKey(e => e.FeatureId);
        builder.Property(e => e.ConfigJson).HasMaxLength(2000);
    }
}

public class TenantFeatureOverrideConfiguration : IEntityTypeConfiguration<TenantFeatureOverride>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<TenantFeatureOverride> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.FeatureId }).IsUnique();
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId);
        builder.HasOne(e => e.Feature).WithMany().HasForeignKey(e => e.FeatureId);
        builder.Property(e => e.Reason).HasMaxLength(500);
    }
}
