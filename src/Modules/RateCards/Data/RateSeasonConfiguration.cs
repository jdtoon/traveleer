using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Data;

public class RateSeasonConfiguration : IEntityTypeConfiguration<RateSeason>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RateSeason> builder)
    {
        builder.ToTable("RateSeasons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => new { x.RateCardId, x.SortOrder });
        builder.HasIndex(x => new { x.RateCardId, x.StartDate, x.EndDate });
        builder.HasOne(x => x.RateCard)
            .WithMany(x => x.Seasons)
            .HasForeignKey(x => x.RateCardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
