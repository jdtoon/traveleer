using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Data;

public class BookingAssignmentConfiguration : IEntityTypeConfiguration<BookingAssignment>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<BookingAssignment> builder)
    {
        builder.ToTable("BookingAssignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.AssignedByUserId).HasMaxLength(450);
        builder.HasIndex(x => x.BookingId);
        builder.HasIndex(x => new { x.BookingId, x.UserId }).IsUnique();
    }
}
