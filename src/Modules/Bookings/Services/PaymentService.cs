using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;

namespace saas.Modules.Bookings.Services;

public interface IPaymentService
{
    Task<BookingPaymentListDto?> GetBookingPaymentsAsync(Guid bookingId);
    Task<BookingPaymentFormDto> CreateEmptyBookingPaymentAsync(Guid bookingId);
    Task<Guid> CreateBookingPaymentAsync(Guid bookingId, BookingPaymentFormDto dto);
    Task<bool> DeleteBookingPaymentAsync(Guid paymentId);
    Task<SupplierPaymentListDto?> GetSupplierPaymentsAsync(Guid bookingItemId);
    Task<SupplierPaymentFormDto?> CreateEmptySupplierPaymentAsync(Guid bookingItemId);
    Task<Guid> CreateSupplierPaymentAsync(Guid bookingItemId, SupplierPaymentFormDto dto);
}

public class PaymentService : IPaymentService
{
    private readonly TenantDbContext _db;

    public PaymentService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<BookingPaymentListDto?> GetBookingPaymentsAsync(Guid bookingId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.Id, b.BookingRef, b.TotalSelling, b.SellingCurrencyCode, b.TravelStartDate })
            .FirstOrDefaultAsync();

        if (booking is null) return null;

        var payments = await _db.BookingPayments
            .AsNoTracking()
            .Where(p => p.BookingId == bookingId)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new BookingPaymentItemDto
            {
                Id = p.Id,
                Amount = p.Amount,
                CurrencyCode = p.CurrencyCode,
                PaymentDate = p.PaymentDate,
                PaymentMethod = p.PaymentMethod,
                Reference = p.Reference,
                Direction = p.Direction,
                Notes = p.Notes,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        var totalReceived = payments
            .Where(p => p.Direction == PaymentDirection.Received)
            .Sum(p => p.Amount)
            - payments
            .Where(p => p.Direction == PaymentDirection.Refunded)
            .Sum(p => p.Amount);

        return new BookingPaymentListDto
        {
            BookingId = booking.Id,
            BookingRef = booking.BookingRef,
            TotalSelling = booking.TotalSelling,
            TotalReceived = totalReceived,
            ClientBalance = booking.TotalSelling - totalReceived,
            SellingCurrencyCode = booking.SellingCurrencyCode,
            TravelStartDate = booking.TravelStartDate,
            Payments = payments
        };
    }

    public async Task<BookingPaymentFormDto> CreateEmptyBookingPaymentAsync(Guid bookingId)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new { b.SellingCurrencyCode })
            .FirstOrDefaultAsync();

        return new BookingPaymentFormDto
        {
            BookingId = bookingId,
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CurrencyCode = booking?.SellingCurrencyCode ?? "USD"
        };
    }

    public async Task<Guid> CreateBookingPaymentAsync(Guid bookingId, BookingPaymentFormDto dto)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw new InvalidOperationException("Booking not found.");

        var payment = new BookingPayment
        {
            BookingId = bookingId,
            Amount = dto.Amount,
            CurrencyCode = dto.CurrencyCode,
            PaymentDate = dto.PaymentDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference?.Trim(),
            Direction = dto.Direction,
            Notes = dto.Notes?.Trim()
        };

        _db.BookingPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }

    public async Task<bool> DeleteBookingPaymentAsync(Guid paymentId)
    {
        var payment = await _db.BookingPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null) return false;

        _db.BookingPayments.Remove(payment);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<SupplierPaymentListDto?> GetSupplierPaymentsAsync(Guid bookingItemId)
    {
        var item = await _db.BookingItems
            .AsNoTracking()
            .Include(bi => bi.Supplier)
            .Where(bi => bi.Id == bookingItemId)
            .Select(bi => new { bi.Id, bi.ServiceName, bi.SupplierId, SupplierName = bi.Supplier != null ? bi.Supplier.Name : null, bi.CostPrice, bi.CostCurrencyCode, bi.Quantity })
            .FirstOrDefaultAsync();

        if (item is null) return null;

        var payments = await _db.SupplierPayments
            .AsNoTracking()
            .Where(p => p.BookingItemId == bookingItemId)
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new SupplierPaymentItemDto
            {
                Id = p.Id,
                Amount = p.Amount,
                CurrencyCode = p.CurrencyCode,
                PaymentDate = p.PaymentDate,
                PaymentMethod = p.PaymentMethod,
                Reference = p.Reference,
                Direction = p.Direction,
                Notes = p.Notes,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        var totalPaid = payments
            .Where(p => p.Direction == PaymentDirection.Paid)
            .Sum(p => p.Amount)
            - payments
            .Where(p => p.Direction == PaymentDirection.Received)
            .Sum(p => p.Amount);

        var totalCost = item.CostPrice * item.Quantity;

        return new SupplierPaymentListDto
        {
            BookingItemId = item.Id,
            ServiceName = item.ServiceName,
            SupplierId = item.SupplierId,
            SupplierName = item.SupplierName,
            CostPrice = totalCost,
            TotalPaid = totalPaid,
            SupplierBalance = totalCost - totalPaid,
            CostCurrencyCode = item.CostCurrencyCode,
            Payments = payments
        };
    }

    public async Task<SupplierPaymentFormDto?> CreateEmptySupplierPaymentAsync(Guid bookingItemId)
    {
        var item = await _db.BookingItems
            .AsNoTracking()
            .Where(bi => bi.Id == bookingItemId)
            .Select(bi => new { bi.Id, bi.SupplierId, bi.CostCurrencyCode })
            .FirstOrDefaultAsync();

        if (item is null || !item.SupplierId.HasValue) return null;

        return new SupplierPaymentFormDto
        {
            BookingItemId = item.Id,
            SupplierId = item.SupplierId.Value,
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CurrencyCode = item.CostCurrencyCode
        };
    }

    public async Task<Guid> CreateSupplierPaymentAsync(Guid bookingItemId, SupplierPaymentFormDto dto)
    {
        var item = await _db.BookingItems
            .FirstOrDefaultAsync(bi => bi.Id == bookingItemId)
            ?? throw new InvalidOperationException("Booking item not found.");

        if (!item.SupplierId.HasValue)
            throw new InvalidOperationException("Booking item has no supplier assigned.");

        var payment = new SupplierPayment
        {
            BookingItemId = bookingItemId,
            SupplierId = item.SupplierId.Value,
            Amount = dto.Amount,
            CurrencyCode = dto.CurrencyCode,
            PaymentDate = dto.PaymentDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference?.Trim(),
            Direction = dto.Direction,
            Notes = dto.Notes?.Trim()
        };

        _db.SupplierPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
