using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Reports.DTOs;
using saas.Modules.Reports.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Reports.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(ReportFeatures.Reports)]
[Route("{slug}/reports")]
public class ReportController : SwapController
{
    private readonly IReportService _service;
    private readonly ICurrentUser _currentUser;

    public ReportController(IReportService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> Index([FromQuery] string range = "month")
    {
        var model = await _service.GetDashboardAsync(_currentUser.UserId!, range);
        return SwapView(model);
    }

    // ========== WIDGET PARTIALS ==========

    [HttpGet("widget/revenue-monthly")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> RevenueMonthly([FromQuery] string range = "month")
    {
        var model = await _service.GetRevenueMonthlyAsync(range);
        return PartialView("_RevenueMonthly", model);
    }

    [HttpGet("widget/revenue-ytd")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> RevenueYtd()
    {
        var model = await _service.GetRevenueYtdAsync();
        return PartialView("_RevenueYtd", model);
    }

    [HttpGet("widget/bookings-status")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> BookingsStatus([FromQuery] string range = "month")
    {
        var model = await _service.GetBookingsByStatusAsync(range);
        return PartialView("_BookingsStatus", model);
    }

    [HttpGet("widget/bookings-recent")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> BookingsRecent()
    {
        var model = await _service.GetRecentBookingsAsync();
        return PartialView("_BookingsRecent", model);
    }

    [HttpGet("widget/quotes-conversion")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> QuotesConversion([FromQuery] string range = "month")
    {
        var model = await _service.GetQuoteConversionAsync(range);
        return PartialView("_QuotesConversion", model);
    }

    [HttpGet("widget/quotes-pipeline")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> QuotesPipeline([FromQuery] string range = "month")
    {
        var model = await _service.GetQuotePipelineAsync(range);
        return PartialView("_QuotesPipeline", model);
    }

    [HttpGet("widget/clients-top")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> ClientsTop([FromQuery] string range = "month")
    {
        var model = await _service.GetTopClientsAsync(range);
        return PartialView("_ClientsTop", model);
    }

    [HttpGet("widget/suppliers-top")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> SuppliersTop([FromQuery] string range = "month")
    {
        var model = await _service.GetTopSuppliersAsync(range);
        return PartialView("_SuppliersTop", model);
    }

    [HttpGet("widget/profitability-summary")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> ProfitabilitySummary([FromQuery] string range = "month")
    {
        var model = await _service.GetProfitabilitySummaryAsync(range);
        return PartialView("_ProfitabilitySummary", model);
    }

    [HttpGet("widget/profitability-by-booking")]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> ProfitByBooking([FromQuery] string range = "month")
    {
        var model = await _service.GetProfitByBookingAsync(range);
        return PartialView("_ProfitByBooking", model);
    }

    // ========== PREFERENCES ==========

    [HttpPost("preferences")]
    [ValidateAntiForgeryToken]
    [HasPermission(ReportPermissions.ReportsRead)]
    public async Task<IActionResult> SavePreferences([FromForm] Dictionary<string, bool> visibility)
    {
        await _service.SavePreferencesAsync(_currentUser.UserId!, visibility);
        return SwapResponse().WithSuccessToast("Preferences saved.").Build();
    }
}
