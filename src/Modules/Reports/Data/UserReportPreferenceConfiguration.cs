using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Reports.Entities;

namespace saas.Modules.Reports.Data;

public class UserReportPreferenceConfiguration : IEntityTypeConfiguration<UserReportPreference>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserReportPreference> builder)
    {
        builder.ToTable("UserReportPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.WidgetKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.WidgetKey }).IsUnique();
    }
}
