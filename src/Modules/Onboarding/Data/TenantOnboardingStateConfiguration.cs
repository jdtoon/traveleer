using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Onboarding.Entities;

namespace saas.Modules.Onboarding.Data;

public class TenantOnboardingStateConfiguration : IEntityTypeConfiguration<TenantOnboardingState>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<TenantOnboardingState> builder)
    {
        builder.ToTable("TenantOnboardingStates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PreferredWorkspace).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Version).HasDefaultValue(1);
    }
}
