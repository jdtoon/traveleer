using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Communications.DTOs;
using saas.Modules.Communications.Entities;
using saas.Modules.Communications.Events;
using saas.Modules.Communications.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Communications.Controllers;

[Authorize(Policy = "TenantUser")]
[Route("{slug}/comms")]
public class CommunicationController : SwapController
{
    private readonly ICommunicationService _service;
    private readonly ICurrentUser _currentUser;

    public CommunicationController(ICommunicationService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("client/{clientId:guid}")]
    public async Task<IActionResult> ClientComms(Guid clientId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var model = await _service.GetByClientAsync(clientId, page, pageSize);
        return PartialView("_CommunicationList", model);
    }

    [HttpGet("booking/{bookingId:guid}")]
    public async Task<IActionResult> BookingComms(Guid bookingId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var model = await _service.GetByBookingAsync(bookingId, page, pageSize);
        return PartialView("_CommunicationList", model);
    }

    [HttpGet("supplier/{supplierId:guid}")]
    public async Task<IActionResult> SupplierComms(Guid supplierId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var model = await _service.GetBySupplierAsync(supplierId, page, pageSize);
        return PartialView("_CommunicationList", model);
    }

    [HttpGet("new")]
    public IActionResult New(Guid? clientId, Guid? supplierId, Guid? bookingId)
    {
        var model = new CreateCommunicationDto
        {
            ClientId = clientId,
            SupplierId = supplierId,
            BookingId = bookingId,
            Direction = CommunicationDirection.Outbound
        };
        return PartialView("_CommunicationForm", model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateCommunicationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return SwapResponse()
                .WithErrorToast("Content is required.")
                .Build();
        }

        await _service.CreateAsync(dto);

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Communication logged.")
            .WithTrigger(CommunicationEvents.Refresh)
            .Build();
    }

    [HttpGet("edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var entry = await _service.GetByIdAsync(id);
        if (entry is null) return NotFound();

        var model = new UpdateCommunicationDto
        {
            Channel = entry.Channel,
            Direction = entry.Direction,
            Subject = entry.Subject,
            Content = entry.Content,
            OccurredAt = entry.OccurredAt
        };
        ViewData["EntryId"] = id;
        return PartialView("_CommunicationEditForm", model);
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateCommunicationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return SwapResponse()
                .WithErrorToast("Content is required.")
                .Build();
        }

        await _service.UpdateAsync(id, dto);

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Communication updated.")
            .WithTrigger(CommunicationEvents.Refresh)
            .Build();
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Communication deleted.")
            .WithTrigger(CommunicationEvents.Refresh)
            .Build();
    }
}
