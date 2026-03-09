using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Data;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BookingRef).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ClientReference).HasMaxLength(100);
        builder.Property(x => x.LeadGuestName).HasMaxLength(200);
        builder.Property(x => x.LeadGuestNationality).HasMaxLength(100);
        builder.Property(x => x.CostCurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.SellingCurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.InternalNotes).HasMaxLength(4000);
        builder.Property(x => x.SpecialRequests).HasMaxLength(4000);
        builder.Property(x => x.TotalCost).HasPrecision(18, 2);
        builder.Property(x => x.TotalSelling).HasPrecision(18, 2);
        builder.Property(x => x.TotalProfit).HasPrecision(18, 2);
        builder.HasIndex(x => x.BookingRef).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.TravelStartDate);
        builder.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
