using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Data;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ContactName).HasMaxLength(120);
        builder.Property(x => x.ContactEmail).HasMaxLength(320);
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.RegistrationNumber).HasMaxLength(100);
        builder.Property(x => x.BankDetails).HasMaxLength(500);
        builder.Property(x => x.PaymentTerms).HasMaxLength(200);
        builder.Property(x => x.DefaultCommissionPercentage).HasPrecision(6, 2);
        builder.Property(x => x.DefaultCurrencyCode).HasMaxLength(10);
        builder.Property(x => x.Website).HasMaxLength(500);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.HasIndex(x => x.Name);
    }
}
