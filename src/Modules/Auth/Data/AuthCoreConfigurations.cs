using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Core;
using saas.Modules.Auth.Entities;

namespace saas.Modules.Auth.Data;

public class MagicLinkTokenConfiguration : IEntityTypeConfiguration<MagicLinkToken>, ICoreEntityConfiguration
{
    public void Configure(EntityTypeBuilder<MagicLinkToken> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Token).IsRequired().HasMaxLength(500);
        builder.HasIndex(e => e.Token);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.TenantSlug).HasMaxLength(100);
    }
}
