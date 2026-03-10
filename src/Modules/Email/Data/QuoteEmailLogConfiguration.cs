using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Email.Entities;
using saas.Modules.Quotes.Entities;

namespace saas.Modules.Email.Data;

public class QuoteEmailLogConfiguration : IEntityTypeConfiguration<QuoteEmailLog>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<QuoteEmailLog> builder)
    {
        builder.ToTable("QuoteEmailLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ToEmail).IsRequired().HasMaxLength(320);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CustomMessage).HasMaxLength(2000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.Property(x => x.SentByDisplayName).HasMaxLength(200);
        builder.Property(x => x.SentByEmail).HasMaxLength(320);
        builder.HasIndex(x => new { x.QuoteId, x.CreatedAt });

        builder.HasOne(x => x.Quote)
            .WithMany()
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
