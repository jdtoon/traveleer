using saas.Data;

namespace saas.Modules.Bookings.Entities;

public enum ActivityType
{
    StatusChange = 0,
    ItemAdded = 1,
    ItemUpdated = 2,
    SupplierRequested = 3,
    VoucherGenerated = 4,
    PaymentRecorded = 5,
    CommentAdded = 6,
    Assigned = 7,
    Unassigned = 8
}

public class ActivityEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public ActivityType ActivityType { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
