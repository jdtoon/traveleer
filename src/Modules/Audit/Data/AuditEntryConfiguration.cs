using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Audit;

namespace saas.Modules.Audit.Data;

public class AuditEntryConfiguration : IEntityTypeConfiguration<Entities.AuditEntry>, IAuditEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Entities.AuditEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Source).HasMaxLength(20).HasDefaultValue("Tenant");
        builder.Property(e => e.TenantSlug).HasMaxLength(100);
        builder.HasIndex(e => e.Source);
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EntityId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(50);
        builder.Property(e => e.UserId).HasMaxLength(200);
        builder.Property(e => e.UserEmail).HasMaxLength(256);
        builder.Property(e => e.IpAddress).HasMaxLength(50);
        builder.Property(e => e.UserAgent).HasMaxLength(500);

        builder.HasIndex(e => e.TenantSlug);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}
