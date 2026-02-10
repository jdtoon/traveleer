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
    }
}
