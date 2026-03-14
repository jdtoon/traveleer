using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Tasks.Entities;

namespace saas.Modules.Tasks.Data;

public class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        builder.ToTable("AgentTasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description)
            .HasMaxLength(2000);

        builder.Property(t => t.AssigneeUserId)
            .HasMaxLength(450);

        builder.Property(t => t.LinkedEntityType)
            .HasMaxLength(50);

        builder.Property(t => t.CompletedByUserId)
            .HasMaxLength(450);

        builder.Property(t => t.CreatedBy)
            .HasMaxLength(450);

        builder.Property(t => t.UpdatedBy)
            .HasMaxLength(450);

        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.AssigneeUserId);
        builder.HasIndex(t => t.DueDate);
        builder.HasIndex(t => new { t.LinkedEntityType, t.LinkedEntityId });
    }
}
