using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Modules.Portal.Entities;

namespace saas.Modules.Portal.Data;

public class ClientActionConfiguration : IEntityTypeConfiguration<ClientAction>
{
    public void Configure(EntityTypeBuilder<ClientAction> builder)
    {
        builder.ToTable("ClientActions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.AcknowledgedByUserId).HasMaxLength(450);
        builder.Property(x => x.CreatedBy).HasMaxLength(450);
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);

        builder.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PortalSession)
            .WithMany()
            .HasForeignKey(x => x.PortalSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
