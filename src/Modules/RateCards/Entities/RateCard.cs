using saas.Data;
using saas.Modules.Inventory.Entities;
using saas.Modules.Settings.Entities;

namespace saas.Modules.RateCards.Entities;

public enum RateCardStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3
}

public class RateCard : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public RateCardStatus Status { get; set; } = RateCardStatus.Draft;
    public Guid InventoryItemId { get; set; }
    public Guid? DefaultMealPlanId { get; set; }
    public string ContractCurrencyCode { get; set; } = "USD";
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Notes { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public MealPlan? DefaultMealPlan { get; set; }
    public ICollection<RateSeason> Seasons { get; set; } = [];
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class RateSeason
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RateCardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int SortOrder { get; set; }
    public bool IsBlackout { get; set; }
    public string? Notes { get; set; }
    public RateCard? RateCard { get; set; }
    public ICollection<RoomRate> Rates { get; set; } = [];
}

public class RoomRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RateSeasonId { get; set; }
    public Guid? RoomTypeId { get; set; }
    public Guid? RateCategoryId { get; set; }
    public decimal WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; } = true;
    public RateSeason? RateSeason { get; set; }
    public RoomType? RoomType { get; set; }
    public RateCategory? RateCategory { get; set; }
}
