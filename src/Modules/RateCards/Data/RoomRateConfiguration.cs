using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Data;

public class RoomRateConfiguration : IEntityTypeConfiguration<RoomRate>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RoomRate> builder)
    {
        builder.ToTable("RoomRates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.WeekdayRate).HasPrecision(18, 2);
        builder.Property(x => x.WeekendRate).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.RateSeasonId, x.RoomTypeId }).IsUnique();
        builder.HasOne(x => x.RateSeason)
            .WithMany(x => x.Rates)
            .HasForeignKey(x => x.RateSeasonId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.RoomType)
            .WithMany()
            .HasForeignKey(x => x.RoomTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
