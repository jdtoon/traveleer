using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Data;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ContactName).HasMaxLength(120);
        builder.Property(x => x.ContactEmail).HasMaxLength(320);
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => x.Name);
    }
}
