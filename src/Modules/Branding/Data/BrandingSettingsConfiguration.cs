using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Modules.Branding.Entities;
using saas.Data.Tenant;

namespace saas.Modules.Branding.Data;

public class BrandingSettingsConfiguration : IEntityTypeConfiguration<BrandingSettings>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<BrandingSettings> builder)
    {
        builder.ToTable("BrandingSettings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SingletonKey).IsUnique();

        builder.Property(x => x.AgencyName).HasMaxLength(200);
        builder.Property(x => x.PublicContactEmail).HasMaxLength(320);
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.Website).HasMaxLength(300);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.LogoUrl).HasMaxLength(500);
        builder.Property(x => x.PrimaryColor).IsRequired().HasMaxLength(20);
        builder.Property(x => x.SecondaryColor).IsRequired().HasMaxLength(20);
        builder.Property(x => x.QuotePrefix).IsRequired().HasMaxLength(20);
        builder.Property(x => x.QuoteNumberFormat).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DefaultQuoteMarkupPercentage).HasPrecision(6, 2);
        builder.Property(x => x.PdfFooterText).HasMaxLength(1000);
    }
}
