using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Itineraries.DTOs;
using saas.Modules.Itineraries.Events;
using saas.Modules.Itineraries.Services;
using Swap.Htmx;

namespace saas.Modules.Itineraries.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(ItineraryFeatures.Itineraries)]
[Route("{slug}/itineraries")]
public class ItineraryController : SwapController
{
    private readonly IItineraryService _service;

    public ItineraryController(IItineraryService service)
    {
        _service = service;
    }

    // ========== ITINERARY CRUD ==========

    [HttpGet("")]
    [HasPermission(ItineraryPermissions.ItinerariesRead)]
    public IActionResult Index([FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(ItineraryPermissions.ItinerariesRead)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        var model = await _service.GetListAsync(status, search);
        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(ItineraryPermissions.ItinerariesCreate)]
    public async Task<IActionResult> New()
        => PartialView("_Form", await _service.CreateEmptyAsync());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesCreate)]
    public async Task<IActionResult> Create([FromForm] ItineraryFormDto dto)
    {
        if (!ModelState.IsValid)
        {
            var empty = await _service.CreateEmptyAsync();
            dto.ClientOptions = empty.ClientOptions;
            dto.BookingOptions = empty.BookingOptions;
            return InvalidForm("_Form", dto);
        }

        var id = await _service.CreateAsync(dto);
        Response.Headers["HX-Redirect"] = Url.Action("Details", new { slug = RouteData.Values["slug"], id });
        return SwapResponse().WithSuccessToast("Itinerary created.").Build();
    }

    [HttpGet("details/{id:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : SwapView(model);
    }

    [HttpGet("edit/{id:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await _service.GetFormAsync(id);
        return model is null ? NotFound() : PartialView("_Form", model);
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> Update(Guid id, [FromForm] ItineraryFormDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
        {
            var empty = await _service.CreateEmptyAsync();
            dto.ClientOptions = empty.ClientOptions;
            dto.BookingOptions = empty.BookingOptions;
            return InvalidForm("_Form", dto);
        }

        await _service.UpdateAsync(id, dto);
        return SuccessResponse("Itinerary updated.", ItineraryEvents.DetailsRefresh);
    }

    [HttpGet("delete-confirm/{id:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesDelete)]
    public async Task<IActionResult> DeleteConfirm(Guid id)
    {
        var item = await _service.GetDetailsAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", new ItineraryDeleteConfirmDto
        {
            Title = $"Delete \"{item.Title}\"?",
            Message = "This removes the itinerary and all its days and items.",
            DeleteUrl = Url.Action("Delete", new { slug = RouteData.Values["slug"], id })!
        });
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        Response.Headers["HX-Redirect"] = Url.Action(nameof(Index), new { slug = RouteData.Values["slug"] }) ?? string.Empty;
        return SwapResponse().WithSuccessToast("Itinerary deleted.").Build();
    }

    // ========== STATUS ==========

    [HttpPost("publish/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> Publish(Guid id)
    {
        await _service.PublishAsync(id);
        return SuccessResponse("Itinerary published.", ItineraryEvents.DetailsRefresh);
    }

    [HttpPost("archive/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> Archive(Guid id)
    {
        await _service.ArchiveAsync(id);
        return SuccessResponse("Itinerary archived.", ItineraryEvents.DetailsRefresh);
    }

    [HttpPost("share/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesShare)]
    public async Task<IActionResult> Share(Guid id)
    {
        await _service.GenerateShareTokenAsync(id);
        return SuccessResponse("Share link generated.", ItineraryEvents.DetailsRefresh);
    }

    // ========== DAYS ==========

    [HttpGet("days/{itineraryId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesRead)]
    public async Task<IActionResult> Days(Guid itineraryId)
    {
        var days = await _service.GetDaysAsync(itineraryId);
        ViewData["ItineraryId"] = itineraryId;
        return PartialView("_DayList", days);
    }

    [HttpGet("days/new/{itineraryId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> NewDay(Guid itineraryId)
        => PartialView("_DayForm", await _service.CreateEmptyDayAsync(itineraryId));

    [HttpGet("days/edit/{dayId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> EditDay(Guid dayId)
    {
        var model = await _service.GetDayFormAsync(dayId);
        return model is null ? NotFound() : PartialView("_DayForm", model);
    }

    [HttpPost("days/create/{itineraryId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> CreateDay(Guid itineraryId, [FromForm] ItineraryDayDto dto)
    {
        dto.ItineraryId = itineraryId;
        if (!ModelState.IsValid)
            return InvalidForm("_DayForm", dto);

        await _service.CreateDayAsync(dto);
        return SuccessResponse("Day added.", ItineraryEvents.DaysRefresh);
    }

    [HttpPost("days/update/{dayId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> UpdateDay(Guid dayId, [FromForm] ItineraryDayDto dto)
    {
        dto.Id = dayId;
        if (!ModelState.IsValid)
            return InvalidForm("_DayForm", dto);

        await _service.UpdateDayAsync(dayId, dto);
        return SuccessResponse("Day updated.", ItineraryEvents.DaysRefresh);
    }

    [HttpGet("days/delete-confirm/{dayId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesDelete)]
    public async Task<IActionResult> DeleteDayConfirm(Guid dayId)
    {
        var day = await _service.GetDayFormAsync(dayId);
        return day is null ? NotFound() : PartialView("_DeleteConfirm", new ItineraryDeleteConfirmDto
        {
            Title = $"Delete \"{day.Title}\"?",
            Message = "This removes the day and all its items.",
            DeleteUrl = Url.Action("DeleteDay", new { slug = RouteData.Values["slug"], dayId })!
        });
    }

    [HttpPost("days/delete/{dayId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> DeleteDay(Guid dayId)
    {
        await _service.DeleteDayAsync(dayId);
        return SuccessResponse("Day removed.", ItineraryEvents.DaysRefresh);
    }

    // ========== ITEMS ==========

    [HttpGet("items/new/{dayId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> NewItem(Guid dayId)
        => PartialView("_ItemForm", await _service.CreateEmptyItemAsync(dayId));

    [HttpGet("items/edit/{itemId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> EditItem(Guid itemId)
    {
        var model = await _service.GetItemFormAsync(itemId);
        return model is null ? NotFound() : PartialView("_ItemForm", model);
    }

    [HttpPost("items/create/{dayId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> CreateItem(Guid dayId, [FromForm] ItineraryItemDto dto)
    {
        dto.ItineraryDayId = dayId;
        if (!ModelState.IsValid)
        {
            dto.InventoryOptions = (await _service.CreateEmptyItemAsync(dayId)).InventoryOptions;
            return InvalidForm("_ItemForm", dto);
        }

        await _service.CreateItemAsync(dto);
        return SuccessResponse("Item added.", ItineraryEvents.DaysRefresh);
    }

    [HttpPost("items/update/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromForm] ItineraryItemDto dto)
    {
        dto.Id = itemId;
        if (!ModelState.IsValid)
        {
            dto.InventoryOptions = (await _service.CreateEmptyItemAsync(dto.ItineraryDayId)).InventoryOptions;
            return InvalidForm("_ItemForm", dto);
        }

        await _service.UpdateItemAsync(itemId, dto);
        return SuccessResponse("Item updated.", ItineraryEvents.DaysRefresh);
    }

    [HttpGet("items/delete-confirm/{itemId:guid}")]
    [HasPermission(ItineraryPermissions.ItinerariesDelete)]
    public async Task<IActionResult> DeleteItemConfirm(Guid itemId)
    {
        var item = await _service.GetItemFormAsync(itemId);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", new ItineraryDeleteConfirmDto
        {
            Title = $"Delete \"{item.Title}\"?",
            Message = "This removes the item from this day.",
            DeleteUrl = Url.Action("DeleteItem", new { slug = RouteData.Values["slug"], itemId })!
        });
    }

    [HttpPost("items/delete/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ItineraryPermissions.ItinerariesEdit)]
    public async Task<IActionResult> DeleteItem(Guid itemId)
    {
        await _service.DeleteItemAsync(itemId);
        return SuccessResponse("Item removed.", ItineraryEvents.DaysRefresh);
    }

    // ========== HELPERS ==========

    private IActionResult InvalidForm(string viewName, object model)
        => SwapResponse().WithErrorToast("Please fix the errors below.").WithView(viewName, model).Build();

    private IActionResult SuccessResponse(string message, string trigger)
        => SwapResponse().WithView("_ModalClose").WithSuccessToast(message).WithTrigger(trigger).Build();
}
