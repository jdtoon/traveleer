namespace saas.Modules.Reports.DTOs;

public class ReportDashboardDto
{
    public string DateRange { get; set; } = "month";
    public List<WidgetDto> Widgets { get; set; } = [];
}

public class WidgetDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
}

public class RevenueMonthlyDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class RevenueYtdDto
{
    public decimal CurrentYear { get; set; }
    public decimal PriorYear { get; set; }
    public decimal PercentChange { get; set; }
}

public class BookingStatusDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class RecentBookingDto
{
    public Guid Id { get; set; }
    public Guid? ClientId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalSelling { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class QuoteConversionDto
{
    public int TotalQuotes { get; set; }
    public int AcceptedQuotes { get; set; }
    public decimal ConversionRate { get; set; }
}

public class QuotePipelineDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalValue { get; set; }
}

public class TopClientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BookingCount { get; set; }
    public decimal TotalValue { get; set; }
}

public class TopSupplierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BookingItemCount { get; set; }
    public decimal TotalCost { get; set; }
}

public class ProfitabilitySummaryDto
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal MarginPercent { get; set; }
}

public class BookingProfitDto
{
    public Guid Id { get; set; }
    public Guid? ClientId { get; set; }
    public string BookingRef { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public decimal TotalSelling { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Profit { get; set; }
    public decimal MarginPercent { get; set; }
}
