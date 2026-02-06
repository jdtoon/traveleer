using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Modules.Notes.Entities;

namespace saas.Data.Tenant.Configurations;

public class NoteConfiguration : IEntityTypeConfiguration<Note>
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
