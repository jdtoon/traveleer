using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Quotes.Entities;

namespace saas.Modules.Quotes.Data;

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("Quotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ClientName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ClientEmail).HasMaxLength(320);
        builder.Property(x => x.ClientPhone).HasMaxLength(50);
        builder.Property(x => x.OutputCurrencyCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.MarkupPercentage).HasPrecision(6, 2);
        builder.Property(x => x.GroupBy).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.InternalNotes).HasMaxLength(4000);
        builder.HasIndex(x => x.ReferenceNumber).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
