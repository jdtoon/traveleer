using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Events;
using saas.Modules.Bookings.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Bookings.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(BookingFeatures.Bookings)]
[Route("{slug}/bookings/payment-links")]
public class PaymentLinkController : SwapController
{
    private readonly IPaymentLinkService _service;
    private readonly ICurrentUser _currentUser;

    public PaymentLinkController(IPaymentLinkService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> List(Guid bookingId)
    {
        var model = await _service.GetByBookingAsync(bookingId);
        return model is null ? NotFound() : PartialView("_PaymentLinkList", model);
    }

    [HttpGet("new/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> New(Guid bookingId)
    {
        var dto = await _service.CreateEmptyFormAsync(bookingId);
        return PartialView("_PaymentLinkForm", dto);
    }

    [HttpPost("create/{bookingId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> Create(Guid bookingId, [FromForm] PaymentLinkFormDto dto)
    {
        dto.BookingId = bookingId;
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_PaymentLinkForm", dto)
                .Build();
        }

        var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
        var userId = _currentUser.UserId ?? string.Empty;
        await _service.CreateAsync(bookingId, dto, userId, slug);

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Payment link created.")
            .WithTrigger(BookingEvents.PaymentLinksRefresh)
            .Build();
    }

    [HttpPost("cancel/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var cancelled = await _service.CancelAsync(id);
        if (!cancelled) return NotFound();

        return SwapResponse()
            .WithSuccessToast("Payment link cancelled.")
            .WithTrigger(BookingEvents.PaymentLinksRefresh)
            .Build();
    }
}
