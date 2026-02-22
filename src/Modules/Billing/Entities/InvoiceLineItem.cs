namespace saas.Modules.Billing.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public LineItemType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }

    // Optional references
    public Guid? AddOnId { get; set; }
    public string? UsageMetric { get; set; }

    public int SortOrder { get; set; }
}

public enum LineItemType
{
    Subscription,
    Seat,
    UsageCharge,
    AddOn,
    SetupFee,
    OneOff,
    Discount,
    Credit,
    Tax,
    Proration
}
