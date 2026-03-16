using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Bookings.Entities;
using saas.Modules.Quotes.Entities;
using saas.Modules.Reports.DTOs;
using saas.Modules.Reports.Entities;

namespace saas.Modules.Reports.Services;

public interface IReportService
{
    Task<ReportDashboardDto> GetDashboardAsync(string userId, string dateRange);
    Task<List<RevenueMonthlyDto>> GetRevenueMonthlyAsync(string dateRange);
    Task<RevenueYtdDto> GetRevenueYtdAsync();
    Task<List<BookingStatusDto>> GetBookingsByStatusAsync(string dateRange);
    Task<List<RecentBookingDto>> GetRecentBookingsAsync();
    Task<QuoteConversionDto> GetQuoteConversionAsync(string dateRange);
    Task<List<QuotePipelineDto>> GetQuotePipelineAsync(string dateRange);
    Task<List<TopClientDto>> GetTopClientsAsync(string dateRange, int top = 10);
    Task<List<TopSupplierDto>> GetTopSuppliersAsync(string dateRange, int top = 10);
    Task<ProfitabilitySummaryDto> GetProfitabilitySummaryAsync(string dateRange);
    Task<List<BookingProfitDto>> GetProfitByBookingAsync(string dateRange);
    Task SavePreferencesAsync(string userId, Dictionary<string, bool> visibility);
}

public class ReportService : IReportService
{
    private readonly TenantDbContext _db;

    private static readonly Dictionary<string, string> WidgetTitles = new()
    {
        ["revenue.monthly"] = "Monthly Revenue",
        ["revenue.ytd"] = "Year-to-Date Revenue",
        ["bookings.status"] = "Bookings by Status",
        ["bookings.recent"] = "Recent Bookings",
        ["quotes.conversion"] = "Quote Conversion Rate",
        ["quotes.pipeline"] = "Quote Pipeline",
        ["clients.top"] = "Top Clients",
        ["suppliers.top"] = "Top Suppliers",
        ["profitability.summary"] = "Profitability Summary",
        ["profitability.by_booking"] = "Profit by Booking"
    };

    public ReportService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<ReportDashboardDto> GetDashboardAsync(string userId, string dateRange)
    {
        var prefs = await _db.Set<UserReportPreference>()
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var widgets = new List<WidgetDto>();
        var sortOrder = 0;

        foreach (var (key, title) in WidgetTitles)
        {
            var pref = prefs.FirstOrDefault(p => p.WidgetKey == key);
            widgets.Add(new WidgetDto
            {
                Key = key,
                Title = title,
                IsVisible = pref?.IsVisible ?? true,
                SortOrder = pref?.SortOrder ?? sortOrder
            });
            sortOrder += 10;
        }

        return new ReportDashboardDto
        {
            DateRange = dateRange,
            Widgets = widgets.OrderBy(w => w.SortOrder).ToList()
        };
    }

    public async Task<List<RevenueMonthlyDto>> GetRevenueMonthlyAsync(string dateRange)
    {
        var (from, to) = GetDateRange(dateRange);

        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status != BookingStatus.Cancelled && b.CreatedAt >= from && b.CreatedAt <= to)
            .Select(b => new { b.CreatedAt, b.TotalSelling })
            .ToListAsync();

        return bookings
            .GroupBy(b => $"{b.CreatedAt.Year}-{b.CreatedAt.Month:D2}")
            .Select(g => new RevenueMonthlyDto
            {
                Month = g.Key,
                Total = g.Sum(b => b.TotalSelling)
            })
            .OrderBy(r => r.Month)
            .ToList();
    }

    public async Task<RevenueYtdDto> GetRevenueYtdAsync()
    {
        var now = DateTime.UtcNow;
        var currentYearStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var priorYearStart = new DateTime(now.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var priorYearEnd = new DateTime(now.Year - 1, now.Month, now.Day, 23, 59, 59, DateTimeKind.Utc);

        var currentYear = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status != BookingStatus.Cancelled && b.CreatedAt >= currentYearStart && b.CreatedAt <= now)
            .SumAsync(b => b.TotalSelling);

        var priorYear = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status != BookingStatus.Cancelled && b.CreatedAt >= priorYearStart && b.CreatedAt <= priorYearEnd)
            .SumAsync(b => b.TotalSelling);

        return new RevenueYtdDto
        {
            CurrentYear = currentYear,
            PriorYear = priorYear,
            PercentChange = priorYear > 0 ? Math.Round((currentYear - priorYear) / priorYear * 100, 1) : 0
        };
    }

    public async Task<List<BookingStatusDto>> GetBookingsByStatusAsync(string dateRange)
    {
        var (from, to) = GetDateRange(dateRange);

        return await _db.Bookings.AsNoTracking()
            .Where(b => b.CreatedAt >= from && b.CreatedAt <= to)
            .GroupBy(b => b.Status)
            .Select(g => new BookingStatusDto
            {
                Status = g.Key.ToString(),
                Count = g.Count()
            })
            .OrderByDescending(b => b.Count)
            .ToListAsync();
    }

    public async Task<List<RecentBookingDto>> GetRecentBookingsAsync()
    {
        return await _db.Bookings.AsNoTracking()
            .Include(b => b.Client)
            .OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .Select(b => new RecentBookingDto
            {
                Id = b.Id,
                ClientId = b.ClientId,
                BookingRef = b.BookingRef,
                ClientName = b.Client != null ? b.Client.Name : null,
                Status = b.Status.ToString(),
                TotalSelling = b.TotalSelling,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<QuoteConversionDto> GetQuoteConversionAsync(string dateRange)
    {
        var (from, to) = GetDateRange(dateRange);

        var total = await _db.Set<Quote>().AsNoTracking()
            .Where(q => q.Status != QuoteStatus.Draft && q.CreatedAt >= from && q.CreatedAt <= to)
            .CountAsync();

        var accepted = await _db.Set<Quote>().AsNoTracking()
            .Where(q => q.Status == QuoteStatus.Accepted && q.CreatedAt >= from && q.CreatedAt <= to)
            .CountAsync();

        return new QuoteConversionDto
        {
            TotalQuotes = total,
            AcceptedQuotes = accepted,
            ConversionRate = total > 0 ? Math.Round((decimal)accepted / total * 100, 1) : 0
        };
    }

    public async Task<List<QuotePipelineDto>> GetQuotePipelineAsync(string dateRange)
    {
        var (from, to) = GetDateRange(dateRange);

        return await _db.Set<Quote>().AsNoTracking()
            .Where(q => q.CreatedAt >= from && q.CreatedAt <= to)
            .GroupBy(q => q.Status)
            .Select(g => new QuotePipelineDto
            {
                Status = g.Key.ToString(),
                Count = g.Count(),
                TotalValue = 0 // Quote doesn't have a direct TotalValue; markup in v1
            })
            .OrderByDescending(q => q.Count)
            .ToListAsync();
    }

    public async Task<List<TopClientDto>> GetTopClientsAsync(string dateRange, int top = 10)
    {
        var (from, to) = GetDateRange(dateRange);

        return await _db.Bookings.AsNoTracking()
            .Where(b => b.Status != BookingStatus.Cancelled && b.CreatedAt >= from && b.CreatedAt <= to)
            .GroupBy(b => new { b.ClientId, b.Client!.Name })
            .Select(g => new TopClientDto
            {
                Id = g.Key.ClientId,
                Name = g.Key.Name,
                BookingCount = g.Count(),
                TotalValue = g.Sum(b => b.TotalSelling),
                MaxBookingCreatedAt = g.Max(b => b.CreatedAt)
            })
            .OrderByDescending(c => c.TotalValue)
            .ThenByDescending(c => c.MaxBookingCreatedAt)
            .Take(top)
            .ToListAsync();
    }

    public async Task<List<TopSupplierDto>> GetTopSuppliersAsync(string dateRange, int top = 10)
    {
        var (from, to) = GetDateRange(dateRange);

        return await _db.BookingItems.AsNoTracking()
            .Where(bi => bi.SupplierId != null
                && bi.Booking != null
                && bi.Booking.Status != BookingStatus.Cancelled
                && bi.Booking.CreatedAt >= from && bi.Booking.CreatedAt <= to)
            .GroupBy(bi => new { bi.SupplierId, bi.Supplier!.Name })
            .Select(g => new TopSupplierDto
            {
                Id = g.Key.SupplierId!.Value,
                Name = g.Key.Name,
                BookingItemCount = g.Count(),
                TotalCost = g.Sum(bi => bi.CostPrice * bi.Quantity),
                MaxBookingCreatedAt = g.Max(bi => bi.Booking!.CreatedAt)
            })
            .OrderByDescending(s => s.TotalCost)
            .ThenByDescending(s => s.MaxBookingCreatedAt)
            .Take(top)
            .ToListAsync();
    }

    public async Task<ProfitabilitySummaryDto> GetProfitabilitySummaryAsync(string dateRange)
    {
        var (from, to) = GetDateRange(dateRange);

        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status != BookingStatus.Cancelled && b.CreatedAt >= from && b.CreatedAt <= to)
            .Select(b => new { b.TotalSelling, b.TotalCost })
            .ToListAsync();

        var totalRevenue = bookings.Sum(b => b.TotalSelling);
        var totalCost = bookings.Sum(b => b.TotalCost);
        var totalProfit = totalRevenue - totalCost;

        return new ProfitabilitySummaryDto
        {
            TotalRevenue = totalRevenue,
            TotalCost = totalCost,
            TotalProfit = totalProfit,
            MarginPercent = totalRevenue > 0 ? Math.Round(totalProfit / totalRevenue * 100, 1) : 0
        };
    }

    public async Task<List<BookingProfitDto>> GetProfitByBookingAsync(string dateRange)
    {
        var (from, to) = GetDateRange(dateRange);

        return await _db.Bookings.AsNoTracking()
            .Include(b => b.Client)
            .Where(b => b.Status != BookingStatus.Cancelled && b.CreatedAt >= from && b.CreatedAt <= to)
            .OrderByDescending(b => b.TotalProfit)
            .ThenByDescending(b => b.CreatedAt)
            .Take(50)
            .Select(b => new BookingProfitDto
            {
                Id = b.Id,
                ClientId = b.ClientId,
                BookingRef = b.BookingRef,
                ClientName = b.Client != null ? b.Client.Name : null,
                TotalSelling = b.TotalSelling,
                TotalCost = b.TotalCost,
                Profit = b.TotalSelling - b.TotalCost,
                MarginPercent = b.TotalSelling > 0 ? Math.Round((b.TotalSelling - b.TotalCost) / b.TotalSelling * 100, 1) : 0
            })
            .ToListAsync();
    }

    public async Task SavePreferencesAsync(string userId, Dictionary<string, bool> visibility)
    {
        var existing = await _db.Set<UserReportPreference>()
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var sortOrder = 0;
        foreach (var (key, isVisible) in visibility)
        {
            if (!WidgetTitles.ContainsKey(key)) continue;

            var pref = existing.FirstOrDefault(p => p.WidgetKey == key);
            if (pref is null)
            {
                pref = new UserReportPreference
                {
                    UserId = userId,
                    WidgetKey = key,
                    IsVisible = isVisible,
                    SortOrder = sortOrder
                };
                _db.Set<UserReportPreference>().Add(pref);
            }
            else
            {
                pref.IsVisible = isVisible;
                pref.SortOrder = sortOrder;
            }
            sortOrder += 10;
        }

        await _db.SaveChangesAsync();
    }

    private static (DateTime from, DateTime to) GetDateRange(string dateRange)
    {
        var now = DateTime.UtcNow;
        return dateRange switch
        {
            "today" => (DateTime.SpecifyKind(now.Date, DateTimeKind.Utc), now),
            "quarter" => (new DateTime(now.Year, (now.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc), now),
            "year" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), now),
            _ => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), now), // month
        };
    }
}
