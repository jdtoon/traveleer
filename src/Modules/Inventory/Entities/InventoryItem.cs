using saas.Data;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Inventory.Entities;

public class InventoryItem : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public InventoryItemKind Kind { get; set; }
    public string? Description { get; set; }
    public decimal BaseCost { get; set; }
    public string? ImageUrl { get; set; }
    public string? Address { get; set; }
    public int? Rating { get; set; }
    public Guid? DestinationId { get; set; }
    public Guid? SupplierId { get; set; }
    public Destination? Destination { get; set; }
    public Supplier? Supplier { get; set; }

    // Transport-specific fields (only used when Kind == Transfer)
    public string? PickupLocation { get; set; }
    public string? DropoffLocation { get; set; }
    public string? VehicleType { get; set; }
    public int? MaxPassengers { get; set; }
    public bool IncludesMeetAndGreet { get; set; }
    public int? TransferDurationMinutes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
