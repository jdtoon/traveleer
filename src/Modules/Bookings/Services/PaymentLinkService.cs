using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Branding.Entities;

namespace saas.Modules.Bookings.Services;

public interface IPaymentLinkService
{
    Task<PaymentLinkListDto?> GetByBookingAsync(Guid bookingId);
    Task<PaymentLinkFormDto> CreateEmptyFormAsync(Guid bookingId);
    Task<PaymentLink> CreateAsync(Guid bookingId, PaymentLinkFormDto dto, string userId, string tenantSlug);
    Task<bool> CancelAsync(Guid id);
    Task<PaymentLinkPublicDto?> GetByTokenAsync(string token);
    Task<bool> MarkAsPaidAsync(string token);
}

public class PaymentLinkService : IPaymentLinkService
{
    private readonly TenantDbContext _db;

    public PaymentLinkService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<PaymentLinkListDto?> GetByBookingAsync(Guid bookingId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.Id, b.BookingRef, b.TotalSelling, b.SellingCurrencyCode, b.ClientId, ClientName = b.Client != null ? b.Client.Name : null })
            .FirstOrDefaultAsync();

        if (booking is null) return null;

        var links = await _db.PaymentLinks
            .AsNoTracking()
            .Where(l => l.BookingId == bookingId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new PaymentLinkItemDto
            {
                Id = l.Id,
                Amount = l.Amount,
                CurrencyCode = l.CurrencyCode,
                Status = l.Status,
                Description = l.Description,
                ExpiresAt = l.ExpiresAt,
                PaidAt = l.PaidAt,
                CreatedAt = l.CreatedAt,
                CreatedBy = l.CreatedBy,
                Token = l.Token
            })
            .ToListAsync();

        // Mark expired links in memory (read-only)
        foreach (var link in links)
        {
            if (link.Status == PaymentLinkStatus.Pending && link.ExpiresAt < DateTime.UtcNow)
                link.Status = PaymentLinkStatus.Expired;
        }

        return new PaymentLinkListDto
        {
            BookingId = booking.Id,
            BookingRef = booking.BookingRef,
            TotalSelling = booking.TotalSelling,
            SellingCurrencyCode = booking.SellingCurrencyCode,
            ClientName = booking.ClientName,
            Links = links
        };
    }

    public async Task<PaymentLinkFormDto> CreateEmptyFormAsync(Guid bookingId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.SellingCurrencyCode })
            .FirstOrDefaultAsync();

        return new PaymentLinkFormDto
        {
            BookingId = bookingId,
            CurrencyCode = booking?.SellingCurrencyCode ?? "USD"
        };
    }

    public async Task<PaymentLink> CreateAsync(Guid bookingId, PaymentLinkFormDto dto, string userId, string tenantSlug)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        var link = new PaymentLink
        {
            BookingId = bookingId,
            ClientId = booking.ClientId,
            Amount = dto.Amount,
            CurrencyCode = dto.CurrencyCode,
            Token = GenerateToken(),
            Description = dto.Description,
            ExpiresAt = DateTime.UtcNow.AddDays(dto.ExpiryDays),
            CreatedByUserId = userId,
            TenantSlug = tenantSlug
        };

        _db.PaymentLinks.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    public async Task<bool> CancelAsync(Guid id)
    {
        var link = await _db.PaymentLinks.FindAsync(id);
        if (link is null || link.Status != PaymentLinkStatus.Pending) return false;

        link.Status = PaymentLinkStatus.Cancelled;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<PaymentLinkPublicDto?> GetByTokenAsync(string token)
    {
        var link = await _db.PaymentLinks
            .AsNoTracking()
            .Include(l => l.Booking)
            .Include(l => l.Client)
            .Where(l => l.Token == token)
            .FirstOrDefaultAsync();

        if (link is null) return null;

        var effectiveStatus = link.Status;
        if (effectiveStatus == PaymentLinkStatus.Pending && link.ExpiresAt < DateTime.UtcNow)
            effectiveStatus = PaymentLinkStatus.Expired;

        var branding = await _db.BrandingSettings.AsNoTracking().FirstOrDefaultAsync();

        return new PaymentLinkPublicDto
        {
            Id = link.Id,
            Amount = link.Amount,
            CurrencyCode = link.CurrencyCode,
            Description = link.Description,
            BookingRef = link.Booking?.BookingRef ?? string.Empty,
            ClientName = link.Client?.Name,
            Status = effectiveStatus,
            ExpiresAt = link.ExpiresAt,
            AgencyName = branding?.AgencyName ?? "Travel Agency",
            LogoUrl = branding?.LogoUrl,
            PrimaryColor = branding?.PrimaryColor ?? "#2563EB",
            TenantSlug = link.TenantSlug,
            Token = link.Token
        };
    }

    public async Task<bool> MarkAsPaidAsync(string token)
    {
        var link = await _db.PaymentLinks
            .FirstOrDefaultAsync(l => l.Token == token);

        if (link is null || link.Status != PaymentLinkStatus.Pending) return false;
        if (link.ExpiresAt < DateTime.UtcNow) return false;

        link.Status = PaymentLinkStatus.Paid;
        link.PaidAt = DateTime.UtcNow;

        // Also create a corresponding BookingPayment record
        var payment = new BookingPayment
        {
            BookingId = link.BookingId,
            Amount = link.Amount,
            CurrencyCode = link.CurrencyCode,
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = PaymentMethod.Online,
            Direction = PaymentDirection.Received,
            Reference = $"Payment Link: {link.Id}",
            Notes = link.Description
        };

        _db.BookingPayments.Add(payment);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
