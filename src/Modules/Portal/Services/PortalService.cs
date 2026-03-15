using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;
using saas.Modules.Portal.DTOs;
using saas.Modules.Portal.Entities;

namespace saas.Modules.Portal.Services;

public interface IPortalService
{
    // Admin link management
    Task<List<PortalLinkListItemDto>> GetLinksAsync(Guid? clientId = null);
    Task<PortalLink> CreateLinkAsync(CreatePortalLinkDto dto, string userId);
    Task RevokeAsync(Guid id);

    // Public portal — token validation
    Task<PortalLink?> ValidateTokenAsync(string token);
    Task<PortalSession> CreateSessionAsync(Guid portalLinkId, Guid clientId, string? ipAddress);
    Task UpdateSessionActivityAsync(Guid sessionId);

    // Public portal — data access
    Task<PortalDashboardDto> GetDashboardAsync(Guid clientId, PortalBrandingDto branding);
    Task<PaginatedList<PortalBookingListItemDto>> GetBookingsAsync(Guid clientId, int page = 1, int pageSize = 12);
    Task<PortalBookingDetailDto?> GetBookingDetailAsync(Guid clientId, Guid bookingId);
    Task<PaginatedList<PortalQuoteListItemDto>> GetQuotesAsync(Guid clientId, int page = 1, int pageSize = 12);
    Task<PaginatedList<PortalDocumentListItemDto>> GetDocumentsAsync(Guid clientId, int page = 1, int pageSize = 12);
}

public class PortalService : IPortalService
{
    private readonly TenantDbContext _db;

    public PortalService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<PortalLinkListItemDto>> GetLinksAsync(Guid? clientId = null)
    {
        var query = _db.PortalLinks.AsNoTracking()
            .Include(l => l.Client)
            .AsQueryable();

        if (clientId.HasValue)
            query = query.Where(l => l.ClientId == clientId.Value);

        return await query.OrderByDescending(l => l.CreatedAt)
            .Select(l => new PortalLinkListItemDto
            {
                Id = l.Id,
                ClientId = l.ClientId,
                ClientName = l.Client != null ? l.Client.Name : "Unknown",
                Scope = l.Scope,
                ExpiresAt = l.ExpiresAt,
                CreatedAt = l.CreatedAt,
                LastAccessedAt = l.LastAccessedAt,
                IsRevoked = l.IsRevoked,
                Token = l.Token
            })
            .ToListAsync();
    }

    public async Task<PortalLink> CreateLinkAsync(CreatePortalLinkDto dto, string userId)
    {
        var token = GenerateToken();
        var link = new PortalLink
        {
            ClientId = dto.ClientId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(dto.ExpiryDays),
            Scope = dto.Scope,
            ScopedEntityId = dto.ScopedEntityId,
            CreatedByUserId = userId
        };

        _db.PortalLinks.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    public async Task RevokeAsync(Guid id)
    {
        var link = await _db.PortalLinks.FindAsync(id);
        if (link is not null)
        {
            link.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<PortalLink?> ValidateTokenAsync(string token)
    {
        var link = await _db.PortalLinks
            .Include(l => l.Client)
            .FirstOrDefaultAsync(l => l.Token == token);

        if (link is null || link.IsRevoked || link.ExpiresAt < DateTime.UtcNow)
            return null;

        link.LastAccessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return link;
    }

    public async Task<PortalSession> CreateSessionAsync(Guid portalLinkId, Guid clientId, string? ipAddress)
    {
        var session = new PortalSession
        {
            PortalLinkId = portalLinkId,
            ClientId = clientId,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            IpAddress = ipAddress
        };

        _db.PortalSessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

    public async Task UpdateSessionActivityAsync(Guid sessionId)
    {
        var session = await _db.PortalSessions.FindAsync(sessionId);
        if (session is not null)
        {
            session.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<PortalDashboardDto> GetDashboardAsync(Guid clientId, PortalBrandingDto branding)
    {
        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        var bookingCount = await _db.Bookings.CountAsync(b => b.ClientId == clientId);
        var quoteCount = await _db.Quotes.CountAsync(q => q.ClientId == clientId);
        var documentCount = await _db.Documents.CountAsync(d => d.ClientId == clientId);

        return new PortalDashboardDto
        {
            ClientName = client?.Name ?? "Client",
            AgencyName = branding.AgencyName,
            LogoUrl = branding.LogoUrl,
            PrimaryColor = branding.PrimaryColor,
            BookingCount = bookingCount,
            QuoteCount = quoteCount,
            DocumentCount = documentCount
        };
    }

    public async Task<PaginatedList<PortalBookingListItemDto>> GetBookingsAsync(Guid clientId, int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Bookings.AsNoTracking()
            .Where(b => b.ClientId == clientId)
            .OrderByDescending(b => b.TravelStartDate)
            .Select(b => new PortalBookingListItemDto
            {
                Id = b.Id,
                Reference = b.BookingRef,
                Destination = b.Items.Any() ? b.Items.First().ServiceName : null,
                StartDate = b.TravelStartDate,
                EndDate = b.TravelEndDate,
                Status = b.Status.ToString()
            });

        return await PaginatedList<PortalBookingListItemDto>.CreateAsync(query, normalizedPage, normalizedPageSize);
    }

    public async Task<PortalBookingDetailDto?> GetBookingDetailAsync(Guid clientId, Guid bookingId)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .Include(b => b.Items).ThenInclude(i => i.Supplier)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.ClientId == clientId);

        if (booking is null) return null;

        return new PortalBookingDetailDto
        {
            Id = booking.Id,
            Reference = booking.BookingRef,
            Destination = booking.Items.FirstOrDefault()?.ServiceName,
            StartDate = booking.TravelStartDate,
            EndDate = booking.TravelEndDate,
            Status = booking.Status.ToString(),
            Notes = booking.SpecialRequests,
            Items = booking.Items.OrderBy(i => i.ServiceDate).Select(i => new PortalBookingItemDto
            {
                ServiceType = i.ServiceKind.ToString(),
                Description = i.ServiceName,
                Date = i.ServiceDate,
                SupplierName = i.Supplier?.Name,
                Status = i.SupplierStatus.ToString()
            }).ToList()
        };
    }

    public async Task<PaginatedList<PortalQuoteListItemDto>> GetQuotesAsync(Guid clientId, int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Quotes.AsNoTracking()
            .Where(q => q.ClientId == clientId)
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new PortalQuoteListItemDto
            {
                Id = q.Id,
                Reference = q.ReferenceNumber,
                Destination = null,
                CreatedAt = q.CreatedAt,
                Status = q.Status.ToString()
            });

        return await PaginatedList<PortalQuoteListItemDto>.CreateAsync(query, normalizedPage, normalizedPageSize);
    }

    public async Task<PaginatedList<PortalDocumentListItemDto>> GetDocumentsAsync(Guid clientId, int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Documents.AsNoTracking()
            .Where(d => d.ClientId == clientId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new PortalDocumentListItemDto
            {
                Id = d.Id,
                FileName = d.FileName,
                DocumentType = d.DocumentType.ToString(),
                FileSize = d.FileSize,
                CreatedAt = d.CreatedAt
            });

        return await PaginatedList<PortalDocumentListItemDto>.CreateAsync(query, normalizedPage, normalizedPageSize);
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
