using saas.Data;
using saas.Modules.Communications.Entities;

namespace saas.Modules.Communications.DTOs;

public record CommunicationEntryDto
{
    public Guid Id { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? BookingId { get; init; }
    public CommunicationChannel Channel { get; init; }
    public CommunicationDirection Direction { get; init; }
    public string? Subject { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    public string LoggedByUserId { get; init; } = string.Empty;
    public string LoggedByName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }

    public string ChannelIcon => Channel switch
    {
        CommunicationChannel.Email => "✉️",
        CommunicationChannel.Phone => "📞",
        CommunicationChannel.WhatsApp => "💬",
        CommunicationChannel.InPerson => "🤝",
        CommunicationChannel.Other => "📝",
        _ => "📝"
    };

    public string DirectionArrow => Direction == CommunicationDirection.Outbound ? "↗" : "↙";
    public string DirectionLabel => Direction == CommunicationDirection.Outbound ? "Outbound" : "Inbound";
}

public record CommunicationListDto
{
    public IReadOnlyList<CommunicationEntryDto> Entries { get; init; } = [];
    public Guid? ClientId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? BookingId { get; init; }
    public string? ContextName { get; init; }
    public int PageIndex { get; init; } = 1;
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public int PageSize { get; init; } = 20;

    public PaginationModel ToPagination(string listUrl, string hxTarget)
        => new(PageIndex, TotalPages, TotalCount, listUrl, hxTarget);
}

public record CreateCommunicationDto
{
    public Guid? ClientId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? BookingId { get; init; }
    public CommunicationChannel Channel { get; init; }
    public CommunicationDirection Direction { get; init; }
    public string? Subject { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime? OccurredAt { get; init; }
}

public record UpdateCommunicationDto
{
    public CommunicationChannel Channel { get; init; }
    public CommunicationDirection Direction { get; init; }
    public string? Subject { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime? OccurredAt { get; init; }
}
