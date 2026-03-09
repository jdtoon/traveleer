using saas.Data;

namespace saas.Modules.Settings.Entities;

public class Currency : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal DefaultMarkup { get; set; }
    public RoundingRule RoundingRule { get; set; } = RoundingRule.None;
    public bool IsBaseCurrency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastManualUpdate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
