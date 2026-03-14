using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Events;
using saas.Modules.Bookings.Services;
using Swap.Htmx;

namespace saas.Modules.Bookings.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(BookingFeatures.Bookings)]
[Route("{slug}/bookings")]
public class PaymentController : SwapController
{
    private readonly IPaymentService _service;

    public PaymentController(IPaymentService service)
    {
        _service = service;
    }

    [HttpGet("payments/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Payments(Guid bookingId)
    {
        var model = await _service.GetBookingPaymentsAsync(bookingId);
        return model is null ? NotFound() : PartialView("_PaymentList", model);
    }

    [HttpGet("payments/new/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> NewPayment(Guid bookingId)
    {
        var dto = await _service.CreateEmptyBookingPaymentAsync(bookingId);
        return PartialView("_PaymentForm", dto);
    }

    [HttpPost("payments/create/{bookingId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> CreatePayment(Guid bookingId, [FromForm] BookingPaymentFormDto dto)
    {
        dto.BookingId = bookingId;
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_PaymentForm", dto)
                .Build();
        }

        await _service.CreateBookingPaymentAsync(bookingId, dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Payment recorded.")
            .WithTrigger(BookingEvents.PaymentsRefresh)
            .Build();
    }

    [HttpPost("payments/delete/{paymentId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> DeletePayment(Guid paymentId)
    {
        var deleted = await _service.DeleteBookingPaymentAsync(paymentId);
        if (!deleted) return NotFound();

        return SwapResponse()
            .WithSuccessToast("Payment removed.")
            .WithTrigger(BookingEvents.PaymentsRefresh)
            .Build();
    }

    [HttpGet("items/payments/{bookingItemId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> SupplierPayments(Guid bookingItemId)
    {
        var model = await _service.GetSupplierPaymentsAsync(bookingItemId);
        return model is null ? NotFound() : PartialView("_SupplierPaymentList", model);
    }

    [HttpGet("items/payments/new/{bookingItemId:guid}")]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> NewSupplierPayment(Guid bookingItemId)
    {
        var dto = await _service.CreateEmptySupplierPaymentAsync(bookingItemId);
        return dto is null ? NotFound() : PartialView("_SupplierPaymentForm", dto);
    }

    [HttpPost("items/payments/create/{bookingItemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> CreateSupplierPayment(Guid bookingItemId, [FromForm] SupplierPaymentFormDto dto)
    {
        dto.BookingItemId = bookingItemId;
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_SupplierPaymentForm", dto)
                .Build();
        }

        await _service.CreateSupplierPaymentAsync(bookingItemId, dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Supplier payment recorded.")
            .WithTrigger(BookingEvents.SupplierPaymentsRefresh)
            .Build();
    }
}
