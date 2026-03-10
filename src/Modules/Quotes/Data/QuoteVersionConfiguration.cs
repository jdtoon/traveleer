using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Quotes.Entities;

namespace saas.Modules.Quotes.Data;

public class QuoteVersionConfiguration : IEntityTypeConfiguration<QuoteVersion>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<QuoteVersion> builder)
    {
        builder.ToTable("QuoteVersions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.SnapshotJson).IsRequired();
        builder.HasIndex(x => new { x.QuoteId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => x.CreatedAt);
        builder.HasOne(x => x.Quote)
            .WithMany(x => x.QuoteVersions)
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}