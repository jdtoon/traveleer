using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Auth.Entities;

namespace saas.Modules.Auth.Data;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(100);
        builder.HasIndex(e => e.Key).IsUnique();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Group).IsRequired().HasMaxLength(100);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(e => new { e.RoleId, e.PermissionId });
        builder.HasOne(e => e.Role).WithMany(r => r.RolePermissions).HasForeignKey(e => e.RoleId);
        builder.HasOne(e => e.Permission).WithMany(p => p.RolePermissions).HasForeignKey(e => e.PermissionId);
    }
}

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(e => e.DisplayName).HasMaxLength(200);
        builder.Property(e => e.AvatarUrl).HasMaxLength(500);
        builder.Property(e => e.TimeZone).HasMaxLength(100);
        builder.Property(e => e.EmailVerificationToken).HasMaxLength(100);
        builder.Property(e => e.TwoFactorSecret).HasMaxLength(100);
        builder.Property(e => e.TwoFactorRecoveryCodes).HasMaxLength(1000);
    }
}

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.IpAddress).HasMaxLength(50);
        builder.Property(e => e.UserAgent).HasMaxLength(500);
        builder.Property(e => e.DeviceInfo).HasMaxLength(200);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ExpiresAt);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
    }
}

public class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(500);
    }
}
