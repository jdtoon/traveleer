using saas.Data;
using saas.Modules.Bookings.Entities;
using saas.Modules.Inventory.Entities;

namespace saas.Modules.Itineraries.Entities;

public class ItineraryItem : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItineraryDayId { get; set; }
    public ItineraryDay? ItineraryDay { get; set; }
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public Guid? BookingItemId { get; set; }
    public BookingItem? BookingItem { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public InventoryItemKind? ItemKind { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
