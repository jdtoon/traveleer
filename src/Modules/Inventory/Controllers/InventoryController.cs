using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Inventory.DTOs;
using saas.Modules.Inventory.Events;
using saas.Modules.Inventory.Services;
using Swap.Htmx;

namespace saas.Modules.Inventory.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(InventoryFeatures.Inventory)]
[Route("{slug}/inventory")]
public class InventoryController : SwapController
{
    private readonly IInventoryService _service;

    public InventoryController(IInventoryService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(InventoryPermissions.InventoryRead)]
    public IActionResult Index([FromQuery] string? type = null, [FromQuery] string? search = null)
    {
        ViewData["Type"] = type;
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(InventoryPermissions.InventoryRead)]
    public async Task<IActionResult> List([FromQuery] string? type = null, [FromQuery] string? search = null, [FromQuery] int page = 1)
    {
        ViewData["Type"] = type;
        ViewData["Search"] = search;
        var model = await _service.GetListAsync(type, search, page);
        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(InventoryPermissions.InventoryCreate)]
    public async Task<IActionResult> New()
        => PartialView("_Form", await _service.CreateEmptyAsync());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(InventoryPermissions.InventoryCreate)]
    public async Task<IActionResult> Create([FromForm] InventoryDto dto)
    {
        if (!ModelState.IsValid)
        {
            var template = await _service.CreateEmptyAsync();
            dto.DestinationOptions = template.DestinationOptions;
            dto.SupplierOptions = template.SupplierOptions;
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        await _service.CreateAsync(dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Inventory item created.")
            .WithTrigger(InventoryEvents.Refresh)
            .Build();
    }

    [HttpGet("edit/{id:guid}")]
    [HasPermission(InventoryPermissions.InventoryEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await _service.GetAsync(id);
        return model is null ? NotFound() : PartialView("_Form", model);
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(InventoryPermissions.InventoryEdit)]
    public async Task<IActionResult> Update(Guid id, [FromForm] InventoryDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
        {
            var template = await _service.CreateEmptyAsync();
            dto.DestinationOptions = template.DestinationOptions;
            dto.SupplierOptions = template.SupplierOptions;
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        await _service.UpdateAsync(id, dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Inventory item updated.")
            .WithTrigger(InventoryEvents.Refresh)
            .Build();
    }

    [HttpGet("delete-confirm/{id:guid}")]
    [HasPermission(InventoryPermissions.InventoryDelete)]
    public async Task<IActionResult> DeleteConfirm(Guid id)
    {
        var model = await _service.GetAsync(id);
        return model is null ? NotFound() : PartialView("_DeleteConfirm", model);
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(InventoryPermissions.InventoryDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Inventory item deleted.")
            .WithTrigger(InventoryEvents.Refresh)
            .Build();
    }
}
