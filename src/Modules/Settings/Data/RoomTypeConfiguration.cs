using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Data;

public class RoomTypeConfiguration : IEntityTypeConfiguration<RoomType>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RoomType> builder)
    {
        builder.ToTable("RoomTypes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder);
    }
}
