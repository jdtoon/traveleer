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
public class DocumentController : SwapController
{
    private readonly IDocumentService _service;
    private readonly ICurrentUser _currentUser;

    public DocumentController(IDocumentService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    // ── Booking documents ────────────────────────────────────────

    [HttpGet("{slug}/bookings/documents/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> BookingDocuments(Guid bookingId)
    {
        var model = await _service.GetBookingDocumentsAsync(bookingId);
        return model is null ? NotFound() : PartialView("_DocumentList", model);
    }

    [HttpGet("{slug}/bookings/documents/new/{bookingId:guid}")]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public IActionResult NewBookingDocument(Guid bookingId)
    {
        var dto = new DocumentUploadDto { BookingId = bookingId };
        return PartialView("_UploadForm", dto);
    }

    [HttpPost("{slug}/bookings/documents/upload/{bookingId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    [RequestSizeLimit(11 * 1024 * 1024)] // slightly above 10 MB to account for form overhead
    public async Task<IActionResult> UploadBookingDocument(Guid bookingId, [FromForm] DocumentUploadDto dto)
    {
        dto.BookingId = bookingId;
        dto.ClientId = null;

        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_UploadForm", dto)
                .Build();
        }

        try
        {
            await _service.UploadAsync(dto, _currentUser.DisplayName ?? _currentUser.Email);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(dto.File), ex.Message);
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("_UploadForm", dto)
                .Build();
        }

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Document uploaded.")
            .WithTrigger(BookingEvents.DocumentsRefresh)
            .Build();
    }

    // ── Client documents ─────────────────────────────────────────

    [HttpGet("{slug}/clients/documents/{clientId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> ClientDocuments(Guid clientId)
    {
        var model = await _service.GetClientDocumentsAsync(clientId);
        return model is null ? NotFound() : PartialView("_DocumentList", model);
    }

    [HttpGet("{slug}/clients/documents/new/{clientId:guid}")]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public IActionResult NewClientDocument(Guid clientId)
    {
        var dto = new DocumentUploadDto { ClientId = clientId };
        return PartialView("_UploadForm", dto);
    }

    [HttpPost("{slug}/clients/documents/upload/{clientId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadClientDocument(Guid clientId, [FromForm] DocumentUploadDto dto)
    {
        dto.ClientId = clientId;
        dto.BookingId = null;

        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_UploadForm", dto)
                .Build();
        }

        try
        {
            await _service.UploadAsync(dto, _currentUser.DisplayName ?? _currentUser.Email);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(dto.File), ex.Message);
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("_UploadForm", dto)
                .Build();
        }

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Document uploaded.")
            .WithTrigger(BookingEvents.ClientDocumentsRefresh)
            .Build();
    }

    // ── Shared (download / delete) ───────────────────────────────

    [HttpGet("{slug}/documents/download/{id:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Download(Guid id)
    {
        var doc = await _service.GetDocumentAsync(id);
        if (doc is null) return NotFound();

        var storage = HttpContext.RequestServices.GetRequiredService<IStorageService>();
        var stream = await storage.DownloadAsync(doc.StorageKey);
        if (stream is null) return NotFound();

        return File(stream, doc.ContentType, doc.FileName);
    }

    [HttpPost("{slug}/documents/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var doc = await _service.GetDocumentAsync(id);
        if (doc is null) return NotFound();

        var trigger = doc.BookingId.HasValue
            ? BookingEvents.DocumentsRefresh
            : BookingEvents.ClientDocumentsRefresh;

        var deleted = await _service.DeleteAsync(id);
        if (!deleted) return NotFound();

        return SwapResponse()
            .WithSuccessToast("Document removed.")
            .WithTrigger(trigger)
            .Build();
    }
}
