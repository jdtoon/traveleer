using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Data;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>, ICoreEntityConfiguration
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

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>, ICoreEntityConfiguration
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

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>, ICoreEntityConfiguration
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

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>, ICoreEntityConfiguration
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
