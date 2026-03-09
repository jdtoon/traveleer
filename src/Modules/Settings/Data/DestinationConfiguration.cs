using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Data;

public class DestinationConfiguration : IEntityTypeConfiguration<Destination>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.ToTable("Destinations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(2);
        builder.Property(x => x.CountryName).HasMaxLength(120);
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.SortOrder);
    }
}
