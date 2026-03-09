using saas.Data;
using saas.Modules.Clients.Entities;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.Quotes.Entities;

public enum QuoteStatus
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Expired = 4
}

public class Quote : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReferenceNumber { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public Guid? ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public string? ClientPhone { get; set; }
    public string OutputCurrencyCode { get; set; } = "USD";
    public decimal MarkupPercentage { get; set; }
    public string GroupBy { get; set; } = "ratecard";
    public DateOnly? ValidUntil { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public bool FilterByTravelDates { get; set; }
    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }
    public Client? Client { get; set; }
    public ICollection<QuoteRateCard> QuoteRateCards { get; set; } = [];
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class QuoteRateCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuoteId { get; set; }
    public Guid RateCardId { get; set; }
    public int SortOrder { get; set; }
    public Quote? Quote { get; set; }
    public RateCard? RateCard { get; set; }
}
