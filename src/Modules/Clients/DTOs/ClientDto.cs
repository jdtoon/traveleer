using System.ComponentModel.DataAnnotations;

namespace saas.Modules.Clients.DTOs;

public class ClientListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public int BookingCount { get; set; }
    public int QuoteCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ClientDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Client name is required.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Company { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(320)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public class ClientDetailsDto : ClientDto
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ClientRecentBookingDto
{
    public Guid Id { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalSelling { get; set; }
    public string SellingCurrencyCode { get; set; } = "USD";
    public DateTime CreatedAt { get; set; }
}

public class ClientRecentQuoteDto
{
    public Guid Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OutputCurrencyCode { get; set; } = "USD";
    public DateTime CreatedAt { get; set; }
}
