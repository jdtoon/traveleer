using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Core;

namespace saas.Modules.SuperAdmin.Data;

public class SuperAdminConfiguration : IEntityTypeConfiguration<Entities.SuperAdmin>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Entities.SuperAdmin> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(e => e.Email).IsUnique();
        builder.Property(e => e.DisplayName).HasMaxLength(200);
        builder.Property(e => e.TwoFactorSecret).HasMaxLength(200);
        builder.Property(e => e.TwoFactorRecoveryCodes).HasMaxLength(500);
    }
}

public class AnnouncementConfiguration : IEntityTypeConfiguration<Entities.Announcement>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Entities.Announcement> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedByEmail).HasMaxLength(256);
    }
}
