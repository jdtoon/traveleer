using saas.Data;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;

namespace saas.Modules.Itineraries.Entities;

public enum ItineraryStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3
}

public class Itinerary : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }
    public string Title { get; set; } = string.Empty;
    public ItineraryStatus Status { get; set; } = ItineraryStatus.Draft;
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Notes { get; set; }
    public string? PublicNotes { get; set; }
    public string? ShareToken { get; set; }
    public DateTime? SharedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public ICollection<ItineraryDay> Days { get; set; } = [];
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
