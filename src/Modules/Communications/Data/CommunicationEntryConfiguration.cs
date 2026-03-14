using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Communications.Entities;

namespace saas.Modules.Communications.Data;

public class CommunicationEntryConfiguration : IEntityTypeConfiguration<CommunicationEntry>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<CommunicationEntry> builder)
    {
        builder.ToTable("CommunicationEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Subject).HasMaxLength(200);
        builder.Property(e => e.Content).HasMaxLength(4000).IsRequired();
        builder.Property(e => e.LoggedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.Channel).IsRequired();
        builder.Property(e => e.Direction).IsRequired();

        builder.HasIndex(e => e.ClientId);
        builder.HasIndex(e => e.SupplierId);
        builder.HasIndex(e => e.BookingId);
        builder.HasIndex(e => e.OccurredAt);
    }
}
