using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Notes.Entities;

namespace saas.Modules.Notes.Data;

public class NoteConfiguration : IEntityTypeConfiguration<Note>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Content).HasMaxLength(4000);
        builder.Property(n => n.Color).HasMaxLength(20).IsRequired();
        builder.HasIndex(n => n.CreatedAt);
    }
}
