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
        builder.Property(e => e.BillingModel).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PaystackMonthlyPlanCode).HasMaxLength(100);
        builder.Property(e => e.PaystackAnnualPlanCode).HasMaxLength(100);
        builder.Ignore(e => e.IsFreePlan);

        // Decimal precision for monetary columns
        builder.Property(e => e.MonthlyPrice).HasPrecision(18, 2);
        builder.Property(e => e.AnnualPrice).HasPrecision(18, 2);
        builder.Property(e => e.PerSeatMonthlyPrice).HasPrecision(18, 2);
        builder.Property(e => e.PerSeatAnnualPrice).HasPrecision(18, 2);
        builder.Property(e => e.SetupFee).HasPrecision(18, 2);

        builder.HasMany(e => e.PricingTiers).WithOne(t => t.Plan).HasForeignKey(t => t.PlanId);
    }
}

public class PlanPricingTierConfiguration : IEntityTypeConfiguration<PlanPricingTier>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<PlanPricingTier> builder)
    {
        builder.HasKey(e => e.Id);
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
        builder.Property(e => e.PaystackAuthorizationCode).HasMaxLength(200);
        builder.Property(e => e.PaystackEmailToken).HasMaxLength(200);
        builder.Property(e => e.PaystackAuthorizationEmail).HasMaxLength(200);

        builder.HasOne(e => e.Plan).WithMany().HasForeignKey(e => e.PlanId);

        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
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
        builder.Property(e => e.CompanyName).HasMaxLength(200);
        builder.Property(e => e.CompanyAddress).HasMaxLength(500);
        builder.Property(e => e.CompanyVatNumber).HasMaxLength(50);
        builder.Property(e => e.TenantCompanyName).HasMaxLength(200);
        builder.Property(e => e.TenantBillingAddress).HasMaxLength(500);
        builder.Property(e => e.TenantVatNumber).HasMaxLength(50);
        builder.Ignore(e => e.Amount);

        // Decimal precision for monetary columns
        builder.Property(e => e.Subtotal).HasPrecision(18, 2);
        builder.Property(e => e.DiscountAmount).HasPrecision(18, 2);
        builder.Property(e => e.TaxAmount).HasPrecision(18, 2);
        builder.Property(e => e.TaxRate).HasPrecision(18, 4);
        builder.Property(e => e.CreditApplied).HasPrecision(18, 2);
        builder.Property(e => e.Total).HasPrecision(18, 2);

        builder.HasOne(e => e.Subscription).WithMany().HasForeignKey(e => e.SubscriptionId);
        builder.HasOne(e => e.Payment).WithOne(p => p.Invoice).HasForeignKey<Payment>(p => p.InvoiceId);
        builder.HasMany(e => e.LineItems).WithOne(li => li.Invoice).HasForeignKey(li => li.InvoiceId);
    }
}

public class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(500);
        builder.Property(e => e.UsageMetric).HasMaxLength(100);
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
        builder.HasIndex(e => e.PaystackReference).IsUnique().HasFilter("PaystackReference IS NOT NULL");
        builder.Property(e => e.PaystackTransactionId).HasMaxLength(200);
        builder.Property(e => e.GatewayResponse).HasMaxLength(4000);

        // Decimal precision for monetary columns
        builder.Property(e => e.Amount).HasPrecision(18, 2);
    }
}

public class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Metric).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => new { e.TenantId, e.Metric, e.PeriodStart });
    }
}

public class AddOnConfiguration : IEntityTypeConfiguration<AddOn>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<AddOn> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Slug).IsUnique();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Currency).HasMaxLength(10);
        builder.Property(e => e.BillingInterval).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PaystackPlanCode).HasMaxLength(100);

        // Decimal precision for monetary columns
        builder.Property(e => e.Price).HasPrecision(18, 2);
    }
}

public class TenantAddOnConfiguration : IEntityTypeConfiguration<TenantAddOn>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<TenantAddOn> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PaystackSubscriptionCode).HasMaxLength(200);
        builder.HasOne(e => e.AddOn).WithMany(a => a.TenantAddOns).HasForeignKey(e => e.AddOnId);
    }
}

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(e => e.Code).IsUnique();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(10);
        builder.Property(e => e.ApplicablePlanSlugs).HasMaxLength(500);

        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        // Decimal precision for monetary columns
        builder.Property(e => e.Value).HasPrecision(18, 2);
    }
}

public class TenantDiscountConfiguration : IEntityTypeConfiguration<TenantDiscount>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<TenantDiscount> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Discount).WithMany(d => d.TenantDiscounts).HasForeignKey(e => e.DiscountId);
    }
}

public class TenantCreditConfiguration : IEntityTypeConfiguration<TenantCredit>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<TenantCredit> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Currency).HasMaxLength(10);
        builder.Property(e => e.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasOne(e => e.ConsumedByInvoice).WithMany().HasForeignKey(e => e.ConsumedByInvoiceId);

        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        // Decimal precision for monetary columns
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.RemainingAmount).HasPrecision(18, 2);
    }
}

public class BillingProfileConfiguration : IEntityTypeConfiguration<BillingProfile>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<BillingProfile> builder)
    {
        builder.HasKey(e => e.TenantId);
        builder.Property(e => e.CompanyName).HasMaxLength(200);
        builder.Property(e => e.BillingAddress).HasMaxLength(500);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.Province).HasMaxLength(100);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(5);
        builder.Property(e => e.VatNumber).HasMaxLength(50);
        builder.Property(e => e.BillingEmail).HasMaxLength(200);
    }
}

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PaystackEventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.PaystackReference).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(e => new { e.PaystackEventType, e.PaystackReference }).IsUnique();
    }
}
