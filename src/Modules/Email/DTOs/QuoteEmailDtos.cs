using System.ComponentModel.DataAnnotations;
using saas.Modules.Email.Entities;

namespace saas.Modules.Email.DTOs;

public class QuoteEmailComposeDto
{
    public Guid QuoteId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public string? TravelWindowLabel { get; set; }
    public string? ValidUntilLabel { get; set; }
    public int RateCardCount { get; set; }
    public string AgencyName { get; set; } = string.Empty;
    public string? ReplyToEmail { get; set; }
    public string? ReplyToPhone { get; set; }

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(320)]
    public string ToEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? CustomMessage { get; set; }
}

public class QuoteEmailHistoryDto
{
    public Guid QuoteId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public List<QuoteEmailHistoryItemDto> Items { get; set; } = [];
}

public class QuoteEmailHistoryItemDto
{
    public Guid Id { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? CustomMessage { get; set; }
    public QuoteEmailDeliveryStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public string? SentByDisplayName { get; set; }
    public string? SentByEmail { get; set; }
}
