using saas.Modules.Portal.Entities;

namespace saas.Modules.Portal.DTOs;

public record PortalLinkListItemDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public PortalLinkScope Scope { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
    public bool IsRevoked { get; init; }
    public string Token { get; init; } = string.Empty;
    public bool IsExpired => ExpiresAt < DateTime.UtcNow;
    public bool IsActive => !IsRevoked && !IsExpired;
}

public record CreatePortalLinkDto
{
    public Guid ClientId { get; init; }
    public PortalLinkScope Scope { get; init; } = PortalLinkScope.Full;
    public Guid? ScopedEntityId { get; init; }
    public int ExpiryDays { get; init; } = 30;
}

public record PortalDashboardDto
{
    public string ClientName { get; init; } = string.Empty;
    public string AgencyName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string PrimaryColor { get; init; } = "#2563EB";
    public int BookingCount { get; init; }
    public int QuoteCount { get; init; }
    public int DocumentCount { get; init; }
}

public record PortalBookingListItemDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string? Destination { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record PortalBookingDetailDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string? Destination { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public List<PortalBookingItemDto> Items { get; init; } = [];
}

public record PortalBookingItemDto
{
    public string ServiceType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateOnly? Date { get; init; }
    public string? SupplierName { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record PortalQuoteListItemDto
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string? Destination { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record PortalDocumentListItemDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record PortalBrandingDto
{
    public string AgencyName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string PrimaryColor { get; init; } = "#2563EB";
    public string SecondaryColor { get; init; } = "#1E3A5F";
    public string PrimaryTextColor { get; init; } = "#FFFFFF";
}
