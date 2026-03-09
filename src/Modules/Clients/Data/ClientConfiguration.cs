using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Clients.Entities;

namespace saas.Modules.Clients.Data;

public class ClientConfiguration : IEntityTypeConfiguration<Client>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Company).HasMaxLength(200);
        builder.Property(c => c.Email).HasMaxLength(320);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.Country).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.Email);
        builder.HasIndex(c => c.CreatedAt);
    }
}
