using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Data;

public class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("SupplierPayments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => x.BookingItemId);
        builder.HasIndex(x => x.SupplierId);
        builder.HasOne(x => x.BookingItem)
            .WithMany()
            .HasForeignKey(x => x.BookingItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
