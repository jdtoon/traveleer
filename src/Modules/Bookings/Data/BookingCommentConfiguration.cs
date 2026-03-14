using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Data;

public class BookingCommentConfiguration : IEntityTypeConfiguration<BookingComment>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<BookingComment> builder)
    {
        builder.ToTable("BookingComments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UserName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => x.BookingId);
    }
}
