using saas.Modules.Inventory.Entities;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Bookings.Entities;

public enum SupplierStatus
{
    NotRequested = 1,
    Requested = 2,
    Confirmed = 3,
    Waitlisted = 4,
    Declined = 5,
    Cancelled = 6
}

public class BookingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public InventoryItemKind ServiceKind { get; set; } = InventoryItemKind.Other;
    public string? Description { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? Nights { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public string CostCurrencyCode { get; set; } = "USD";
    public string SellingCurrencyCode { get; set; } = "USD";
    public int Quantity { get; set; } = 1;
    public int Pax { get; set; } = 1;
    public SupplierStatus SupplierStatus { get; set; } = SupplierStatus.NotRequested;
    public DateTime? RequestedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public bool VoucherSent { get; set; }
    public DateTime? VoucherSentAt { get; set; }
    public bool VoucherGenerated { get; set; }
    public DateTime? VoucherGeneratedAt { get; set; }
    public string? VoucherNumber { get; set; }
    public string? SupplierReference { get; set; }
    public string? SupplierNotes { get; set; }
}
