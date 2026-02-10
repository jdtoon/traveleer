using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Notes.Entities;

namespace saas.Modules.Notes.Data;

public class NoteConfiguration : IEntityTypeConfiguration<Note>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Content).HasMaxLength(4000);
        builder.Property(e => e.Color).HasMaxLength(20);
        builder.HasIndex(e => e.CreatedAt);
    }
}
