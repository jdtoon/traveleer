using System.ComponentModel.DataAnnotations;
using saas.Modules.Inventory.Entities;
using saas.Modules.Itineraries.Entities;

namespace saas.Modules.Itineraries.DTOs;

public class ItineraryListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? BookingRef { get; set; }
    public ItineraryStatus Status { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public int DayCount { get; set; }
}

public class ItineraryFormDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public Guid? BookingId { get; set; }
    public Guid? ClientId { get; set; }

    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [StringLength(2000)]
    public string? PublicNotes { get; set; }

    public List<ClientOptionDto> ClientOptions { get; set; } = [];
    public List<BookingOptionDto> BookingOptions { get; set; } = [];
}

public class ItineraryDetailsDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public Guid? ClientId { get; set; }
    public string? BookingRef { get; set; }
    public Guid? BookingId { get; set; }
    public ItineraryStatus Status { get; set; }
    public DateOnly? TravelStartDate { get; set; }
    public DateOnly? TravelEndDate { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Notes { get; set; }
    public string? PublicNotes { get; set; }
    public string? ShareToken { get; set; }
    public DateTime? SharedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ItineraryDayDto> Days { get; set; } = [];
}

public class ItineraryDayDto
{
    public Guid Id { get; set; }
    public Guid ItineraryId { get; set; }

    public int DayNumber { get; set; }
    public DateOnly? Date { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }
    public List<ItineraryItemDto> Items { get; set; } = [];
}

public class ItineraryItemDto
{
    public Guid Id { get; set; }
    public Guid ItineraryDayId { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    public Guid? InventoryItemId { get; set; }
    public Guid? BookingItemId { get; set; }

    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public int SortOrder { get; set; }
    public InventoryItemKind? ItemKind { get; set; }

    // Display helpers
    public string? InventoryItemName { get; set; }
    public List<InventoryOptionDto> InventoryOptions { get; set; } = [];
}

public class ItineraryDeleteConfirmDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DeleteUrl { get; set; } = string.Empty;
}

public class ClientOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BookingOptionDto
{
    public Guid Id { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string? ClientName { get; set; }
}

public class InventoryOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public InventoryItemKind Kind { get; set; }
}
