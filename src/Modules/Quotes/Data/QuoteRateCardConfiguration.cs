using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Quotes.Entities;

namespace saas.Modules.Quotes.Data;

public class QuoteRateCardConfiguration : IEntityTypeConfiguration<QuoteRateCard>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<QuoteRateCard> builder)
    {
        builder.ToTable("QuoteRateCards");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.QuoteId, x.RateCardId }).IsUnique();
        builder.HasIndex(x => new { x.QuoteId, x.SortOrder });
        builder.HasOne(x => x.Quote)
            .WithMany(x => x.QuoteRateCards)
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.RateCard)
            .WithMany()
            .HasForeignKey(x => x.RateCardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
