using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Events;
using saas.Modules.Bookings.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Bookings.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(BookingFeatures.Bookings)]
[Route("{slug}/bookings")]
public class BookingController : SwapController
{
    private readonly IBookingService _service;
    private readonly ICurrentUser _currentUser;

    public BookingController(IBookingService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public IActionResult Index([FromQuery] string? status = null, [FromQuery] string? search = null, [FromQuery] bool assignedToMe = false, [FromQuery] string? view = null)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        ViewData["AssignedToMe"] = assignedToMe;
        ViewData["View"] = view ?? "list";
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] bool assignedToMe = false, [FromQuery] string? view = null)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        ViewData["AssignedToMe"] = assignedToMe;
        ViewData["View"] = view ?? "list";
        var assignedUserId = assignedToMe ? _currentUser.UserId : null;
        var pageSize = (view == "calendar") ? 100 : 12;
        var model = await _service.GetListAsync(status, search, page, pageSize: pageSize, assignedToUserId: assignedUserId);
        
        if (view == "calendar")
            return PartialView("_Calendar", model);

        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(BookingPermissions.BookingsCreate)]
    public async Task<IActionResult> New()
        => PartialView("_Form", await _service.CreateEmptyAsync());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsCreate)]
    public async Task<IActionResult> Create([FromForm] BookingFormDto dto)
    {
        if (!ModelState.IsValid)
        {
            dto.ClientOptions = (await _service.CreateEmptyAsync()).ClientOptions;
            dto.CurrencyOptions = (await _service.CreateEmptyAsync()).CurrencyOptions;
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        await _service.CreateAsync(dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Booking created.")
            .WithTrigger(BookingEvents.Refresh)
            .Build();
    }

    [HttpPost("convert-from-quote/{quoteId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsCreate)]
    public async Task<IActionResult> ConvertFromQuote(Guid quoteId)
    {
        var result = await _service.ConvertFromQuoteAsync(quoteId);
        if (!result.Success || !result.BookingId.HasValue)
        {
            return SwapResponse()
                .WithErrorToast(result.ErrorMessage ?? "Quote could not be converted to a booking.")
                .Build();
        }

        Response.Headers["HX-Redirect"] = Url.Action(nameof(Details), new { slug = RouteData.Values["slug"], id = result.BookingId.Value }) ?? string.Empty;
        return SwapResponse()
            .WithSuccessToast(result.AlreadyExists ? "Booking already exists for this quote." : "Booking created from quote.")
            .Build();
    }

    [HttpGet("details/{id:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        if (model is not null)
        {
            var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
            Breadcrumbs.Set(ViewData, model.BookingRef, "Bookings", $"/{slug}/bookings");
        }
        return model is null ? NotFound() : SwapView(model);
    }

    [HttpGet("summary/{id:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Summary(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : PartialView("_Summary", model);
    }

    [HttpGet("items/{id:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Items(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : PartialView("_ItemList", model);
    }

    [HttpGet("items/new/{id:guid}")]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> NewItem(Guid id)
        => PartialView("_ItemForm", await _service.CreateEmptyItemAsync(id));

    [HttpPost("items/create/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> CreateItem(Guid id, [FromForm] BookingItemFormDto dto)
    {
        dto.BookingId = id;
        if (!ModelState.IsValid)
        {
            dto.InventoryOptions = (await _service.CreateEmptyItemAsync(id)).InventoryOptions;
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_ItemForm", dto)
                .Build();
        }

        await _service.AddItemAsync(id, dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Booking service added.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }

    [HttpPost("items/request/{bookingId:guid}/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> RequestSupplier(Guid bookingId, Guid itemId)
    {
        var result = await _service.SendSupplierRequestAsync(bookingId, itemId, isReminder: false);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.ErrorMessage ?? "Supplier request could not be sent.")
                .Build();
        }

        return SwapResponse()
            .WithSuccessToast("Supplier request sent.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }

    [HttpPost("items/remind/{bookingId:guid}/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> SendSupplierReminder(Guid bookingId, Guid itemId)
    {
        var result = await _service.SendSupplierRequestAsync(bookingId, itemId, isReminder: true);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.ErrorMessage ?? "Supplier reminder could not be sent.")
                .Build();
        }

        return SwapResponse()
            .WithSuccessToast("Supplier reminder sent.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }

    [HttpPost("items/voucher/generate/{bookingId:guid}/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> GenerateVoucher(Guid bookingId, Guid itemId)
    {
        var result = await _service.GenerateVoucherAsync(bookingId, itemId);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.ErrorMessage ?? "Voucher could not be generated.")
                .Build();
        }

        return SwapResponse()
            .WithSuccessToast("Voucher generated.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }

    [HttpGet("items/voucher/{bookingId:guid}/{itemId:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Voucher(Guid bookingId, Guid itemId)
    {
        var pdf = await _service.GetVoucherPdfAsync(bookingId, itemId);
        if (pdf is null)
        {
            return NotFound();
        }

        return File(pdf.Value.PdfBytes, "application/pdf", pdf.Value.FileName);
    }

    [HttpPost("items/voucher/send/{bookingId:guid}/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> SendVoucher(Guid bookingId, Guid itemId)
    {
        var result = await _service.SendVoucherAsync(bookingId, itemId);
        if (!result.Success)
        {
            return SwapResponse()
                .WithErrorToast(result.ErrorMessage ?? "Voucher could not be sent.")
                .Build();
        }

        return SwapResponse()
            .WithSuccessToast("Voucher sent.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }

    [HttpPost("items/confirm/{bookingId:guid}/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> ConfirmSupplier(Guid bookingId, Guid itemId)
    {
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Confirmed);
        return SwapResponse()
            .WithSuccessToast("Supplier confirmed.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }

    [HttpPost("items/decline/{bookingId:guid}/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(BookingPermissions.BookingsEdit)]
    public async Task<IActionResult> DeclineSupplier(Guid bookingId, Guid itemId)
    {
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Declined);
        return SwapResponse()
            .WithSuccessToast("Supplier declined.")
            .WithTrigger(BookingEvents.ItemsRefresh)
            .Build();
    }
}
