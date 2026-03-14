using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Itineraries.Entities;

namespace saas.Modules.Itineraries.Data;

public class ItineraryConfiguration : IEntityTypeConfiguration<Itinerary>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Itinerary> builder)
    {
        builder.ToTable("Itineraries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CoverImageUrl).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.PublicNotes).HasMaxLength(2000);
        builder.Property(x => x.ShareToken).HasMaxLength(64);
        builder.HasIndex(x => x.BookingId);
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ShareToken).IsUnique();
    }
}

public class ItineraryDayConfiguration : IEntityTypeConfiguration<ItineraryDay>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<ItineraryDay> builder)
    {
        builder.ToTable("ItineraryDays");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasIndex(x => x.ItineraryId);
    }
}

public class ItineraryItemConfiguration : IEntityTypeConfiguration<ItineraryItem>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<ItineraryItem> builder)
    {
        builder.ToTable("ItineraryItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ImageUrl).HasMaxLength(500);
        builder.HasIndex(x => x.ItineraryDayId);
        builder.HasIndex(x => x.InventoryItemId);
        builder.HasIndex(x => x.BookingItemId);
    }
}
