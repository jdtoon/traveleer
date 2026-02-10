using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Tenancy.Data;

public class TenantCoreConfiguration : IEntityTypeConfiguration<Tenant>, ICoreEntityConfiguration
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
