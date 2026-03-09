using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Data;

public class RateCategoryConfiguration : IEntityTypeConfiguration<RateCategory>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RateCategory> builder)
    {
        builder.ToTable("RateCategories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => new { x.ForType, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.ForType, x.SortOrder });
    }
}
