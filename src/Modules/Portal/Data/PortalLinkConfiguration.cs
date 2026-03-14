using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Portal.Entities;

namespace saas.Modules.Portal.Data;

public class PortalLinkConfiguration : IEntityTypeConfiguration<PortalLink>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<PortalLink> builder)
    {
        builder.ToTable("PortalLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Token).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => x.ClientId);
    }
}

public class PortalSessionConfiguration : IEntityTypeConfiguration<PortalSession>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<PortalSession> builder)
    {
        builder.ToTable("PortalSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.HasIndex(x => x.PortalLinkId);
        builder.HasIndex(x => x.ClientId);
    }
}
