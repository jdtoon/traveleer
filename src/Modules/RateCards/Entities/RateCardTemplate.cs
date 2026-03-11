using System.Text.Json.Serialization;
using saas.Data;
using saas.Modules.Inventory.Entities;

namespace saas.Modules.RateCards.Entities;

public class RateCardTemplate : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public InventoryItemKind ForKind { get; set; } = InventoryItemKind.Hotel;
    public string? Description { get; set; }
    public string SeasonsJson { get; set; } = "[]";
    public bool IsSystemTemplate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class TemplateSeasonDefinition
{
    public string Name { get; set; } = string.Empty;
    public int? MonthStart { get; set; }
    public int? DayStart { get; set; }
    public int? MonthEnd { get; set; }
    public int? DayEnd { get; set; }
    public int SortOrder { get; set; }
    public bool IsBlackout { get; set; }
    public string? Notes { get; set; }

    [JsonIgnore]
    public bool UsesDatePattern
        => MonthStart.HasValue && DayStart.HasValue && MonthEnd.HasValue && DayEnd.HasValue;
}
