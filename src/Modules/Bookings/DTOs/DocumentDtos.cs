using System.ComponentModel.DataAnnotations;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.DTOs;

public class DocumentListDto
{
    public Guid? BookingId { get; set; }
    public Guid? ClientId { get; set; }
    public string? ParentName { get; set; }
    public List<DocumentItemDto> Documents { get; set; } = [];
}

public class DocumentItemDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DocumentType DocumentType { get; set; }
    public string? Description { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public string FormattedSize => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };

    public string TypeBadgeClass => DocumentType switch
    {
        DocumentType.Voucher => "badge-primary",
        DocumentType.Invoice => "badge-secondary",
        DocumentType.Passport => "badge-accent",
        DocumentType.Visa => "badge-info",
        DocumentType.Insurance => "badge-warning",
        _ => "badge-ghost"
    };
}

public class DocumentUploadDto
{
    public Guid? BookingId { get; set; }
    public Guid? ClientId { get; set; }

    [Required(ErrorMessage = "Please select a file.")]
    public IFormFile? File { get; set; }

    [Required(ErrorMessage = "Please select a document type.")]
    public DocumentType DocumentType { get; set; } = DocumentType.Other;

    [MaxLength(500)]
    public string? Description { get; set; }
}
