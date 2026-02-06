using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace saas.Data.Core.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Slug).IsUnique();
        builder.Property(e => e.ContactEmail).IsRequired().HasMaxLength(256);
        builder.Property(e => e.DatabaseName).HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(e => e.Plan).WithMany(p => p.Tenants).HasForeignKey(e => e.PlanId);
        builder.HasOne(e => e.ActiveSubscription).WithOne(s => s.Tenant).HasForeignKey<Subscription>(s => s.TenantId);
        builder.HasMany(e => e.Invoices).WithOne(i => i.Tenant).HasForeignKey(i => i.TenantId);
        builder.HasMany(e => e.Payments).WithOne(p => p.Tenant).HasForeignKey(p => p.TenantId);
    }
}

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Slug).IsUnique();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Currency).HasMaxLength(10);
        builder.Property(e => e.PaystackPlanCode).HasMaxLength(100);
    }
}

public class FeatureConfiguration : IEntityTypeConfiguration<Feature>
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

public class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.HasKey(e => new { e.PlanId, e.FeatureId });
        builder.HasOne(e => e.Plan).WithMany(p => p.PlanFeatures).HasForeignKey(e => e.PlanId);
        builder.HasOne(e => e.Feature).WithMany(f => f.PlanFeatures).HasForeignKey(e => e.FeatureId);
        builder.Property(e => e.ConfigJson).HasMaxLength(2000);
    }
}

public class TenantFeatureOverrideConfiguration : IEntityTypeConfiguration<TenantFeatureOverride>
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

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.BillingCycle).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PaystackSubscriptionCode).HasMaxLength(200);
        builder.Property(e => e.PaystackCustomerCode).HasMaxLength(200);

        builder.HasOne(e => e.Plan).WithMany().HasForeignKey(e => e.PlanId);
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.InvoiceNumber).IsRequired().HasMaxLength(50);
        builder.HasIndex(e => e.InvoiceNumber).IsUnique();
        builder.Property(e => e.Currency).HasMaxLength(10);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PaystackReference).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasOne(e => e.Subscription).WithMany().HasForeignKey(e => e.SubscriptionId);
        builder.HasOne(e => e.Payment).WithOne(p => p.Invoice).HasForeignKey<Payment>(p => p.InvoiceId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Currency).HasMaxLength(10);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PaystackReference).HasMaxLength(200);
        builder.Property(e => e.PaystackTransactionId).HasMaxLength(200);
        builder.Property(e => e.GatewayResponse).HasMaxLength(4000);
    }
}

public class SuperAdminConfiguration : IEntityTypeConfiguration<SuperAdmin>
{
    public void Configure(EntityTypeBuilder<SuperAdmin> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(e => e.Email).IsUnique();
        builder.Property(e => e.DisplayName).HasMaxLength(200);
    }
}

public class MagicLinkTokenConfiguration : IEntityTypeConfiguration<MagicLinkToken>
{
    public void Configure(EntityTypeBuilder<MagicLinkToken> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Token).IsRequired().HasMaxLength(500);
        builder.HasIndex(e => e.Token);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.TenantSlug).HasMaxLength(100);
    }
}
