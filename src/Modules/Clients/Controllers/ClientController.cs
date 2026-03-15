using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Clients.DTOs;
using saas.Modules.Clients.Events;
using saas.Modules.Clients.Services;
using Swap.Htmx;

namespace saas.Modules.Clients.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(ClientFeatures.Clients)]
[Route("{slug}/clients")]
public class ClientController : SwapController
{
    private readonly IClientService _service;

    public ClientController(IClientService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(ClientPermissions.ClientsRead)]
    public IActionResult Index([FromQuery] string? search = null)
    {
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(ClientPermissions.ClientsRead)]
    public async Task<IActionResult> List([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        ViewData["Search"] = search;
        var model = await _service.GetListAsync(search, page, pageSize);
        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(ClientPermissions.ClientsCreate)]
    public async Task<IActionResult> New()
    {
        var model = await _service.CreateEmptyAsync();
        return PartialView("_Form", model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(ClientPermissions.ClientsCreate)]
    public async Task<IActionResult> Create([FromForm] ClientDto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        if (!string.IsNullOrWhiteSpace(dto.Email) && await _service.EmailExistsAsync(dto.Email))
        {
            ModelState.AddModelError(nameof(ClientDto.Email), "A client with this email already exists.");
            return SwapResponse()
                .WithErrorToast("A client with this email already exists.")
                .WithView("_Form", dto)
                .Build();
        }

        await _service.CreateAsync(dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Client created.")
            .WithTrigger(ClientEvents.Refresh)
            .Build();
    }

    [HttpGet("edit/{id:guid}")]
    [HasPermission(ClientPermissions.ClientsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await _service.GetAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        return PartialView("_Form", model);
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ClientPermissions.ClientsEdit)]
    public async Task<IActionResult> Update(Guid id, [FromForm] ClientDto dto)
    {
        dto.Id = id;

        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        if (!string.IsNullOrWhiteSpace(dto.Email) && await _service.EmailExistsAsync(dto.Email, id))
        {
            ModelState.AddModelError(nameof(ClientDto.Email), "Another client with this email already exists.");
            return SwapResponse()
                .WithErrorToast("Another client with this email already exists.")
                .WithView("_Form", dto)
                .Build();
        }

        await _service.UpdateAsync(id, dto);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Client updated.")
            .WithTrigger(ClientEvents.Refresh)
            .Build();
    }

    [HttpGet("details/{id:guid}")]
    [HasPermission(ClientPermissions.ClientsRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        return PartialView("_Details", model);
    }

    [HttpGet("delete-confirm/{id:guid}")]
    [HasPermission(ClientPermissions.ClientsDelete)]
    public async Task<IActionResult> DeleteConfirm(Guid id)
    {
        var model = await _service.GetAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        return PartialView("_DeleteConfirm", model);
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(ClientPermissions.ClientsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Client deleted.")
            .WithTrigger(ClientEvents.Refresh)
            .Build();
    }
}