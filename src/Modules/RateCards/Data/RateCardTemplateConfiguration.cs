using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Data;

public class RateCardTemplateConfiguration : IEntityTypeConfiguration<RateCardTemplate>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RateCardTemplate> builder)
    {
        builder.ToTable("RateCardTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.SeasonsJson).HasMaxLength(8000).IsRequired();
        builder.HasIndex(x => new { x.ForKind, x.Name }).IsUnique();
    }
}
