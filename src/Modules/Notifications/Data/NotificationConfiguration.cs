using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Notifications.Entities;

namespace saas.Modules.Notifications.Data;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.UserId).HasMaxLength(450).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(256).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000);
        builder.Property(n => n.Url).HasMaxLength(500);
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(n => new { n.UserId, n.IsRead });
        builder.HasIndex(n => n.CreatedAt);
    }
}
