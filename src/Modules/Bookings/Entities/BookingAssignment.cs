namespace saas.Modules.Bookings.Entities;

public class BookingAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public string? AssignedByUserId { get; set; }
}
