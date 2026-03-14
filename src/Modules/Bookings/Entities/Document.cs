using saas.Data;

namespace saas.Modules.Bookings.Entities;

public enum DocumentType
{
    Voucher = 1,
    Invoice = 2,
    Passport = 3,
    Visa = 4,
    Insurance = 5,
    Other = 6
}

public class Document : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BookingId { get; set; }
    public Guid? ClientId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.Other;
    public string? Description { get; set; }
    public string? UploadedBy { get; set; }

    public Booking? Booking { get; set; }
    public saas.Modules.Clients.Entities.Client? Client { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
