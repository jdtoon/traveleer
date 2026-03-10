using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Data;

public class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.ToTable("BookingItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ServiceName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.CostCurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.SellingCurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.VoucherNumber).HasMaxLength(50);
        builder.Property(x => x.SupplierReference).HasMaxLength(100);
        builder.Property(x => x.SupplierNotes).HasMaxLength(2000);
        builder.Property(x => x.CostPrice).HasPrecision(18, 2);
        builder.Property(x => x.SellingPrice).HasPrecision(18, 2);
        builder.HasIndex(x => x.BookingId);
        builder.HasIndex(x => x.SupplierStatus);
        builder.HasOne(x => x.Booking)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.InventoryItem)
            .WithMany()
            .HasForeignKey(x => x.InventoryItemId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
