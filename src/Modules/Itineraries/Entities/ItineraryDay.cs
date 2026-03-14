using saas.Data;

namespace saas.Modules.Itineraries.Entities;

public class ItineraryDay : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItineraryId { get; set; }
    public Itinerary? Itinerary { get; set; }
    public int DayNumber { get; set; }
    public DateOnly? Date { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public ICollection<ItineraryItem> Items { get; set; } = [];
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
