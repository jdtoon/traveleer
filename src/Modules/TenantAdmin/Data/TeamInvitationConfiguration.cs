using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;

namespace saas.Modules.TenantAdmin.Data;

public class TeamInvitationConfiguration : IEntityTypeConfiguration<Entities.TeamInvitation>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Entities.TeamInvitation> builder)
    {
        builder.ToTable("TeamInvitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Token).HasMaxLength(100).IsRequired();
        builder.Property(i => i.RoleId).HasMaxLength(450);
        builder.Property(i => i.RoleName).HasMaxLength(256);
        builder.Property(i => i.InvitedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(i => i.InvitedByEmail).HasMaxLength(256);
        builder.HasIndex(i => i.Token).IsUnique();
        builder.HasIndex(i => i.Email);
    }
}
