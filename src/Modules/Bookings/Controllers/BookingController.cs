using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Events;
using saas.Modules.Bookings.Services;
using Swap.Htmx;

namespace saas.Modules.Bookings.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(BookingFeatures.Bookings)]
[Route("{slug}/bookings")]
public class BookingController : SwapController
{
    private readonly IBookingService _service;

    public BookingController(IBookingService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public IActionResult Index([FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] string? search = null, [FromQuery] int page = 1)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        var model = await _service.GetListAsync(status, search, page);
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

    [HttpGet("details/{id:guid}")]
    [HasPermission(BookingPermissions.BookingsRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
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
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Requested);
        return SwapResponse()
            .WithSuccessToast("Supplier request recorded.")
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
