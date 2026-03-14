using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Suppliers.Entities;

namespace saas.Modules.Suppliers.Data;

public class SupplierContactConfiguration : IEntityTypeConfiguration<SupplierContact>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<SupplierContact> builder)
    {
        builder.ToTable("SupplierContacts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(100);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.HasIndex(x => x.SupplierId);
    }
}
