using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Data;

public class RateCardConfiguration : IEntityTypeConfiguration<RateCard>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("RateCards");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContractCurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.InventoryItemId, x.Status });
        builder.HasOne(x => x.InventoryItem)
            .WithMany()
            .HasForeignKey(x => x.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DefaultMealPlan)
            .WithMany()
            .HasForeignKey(x => x.DefaultMealPlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
