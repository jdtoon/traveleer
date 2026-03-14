using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Portal.DTOs;
using saas.Modules.Portal.Entities;
using saas.Modules.Portal.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Portal.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(PortalFeatures.Portal)]
public class PortalLinkController : SwapController
{
    private readonly IPortalService _service;
    private readonly ICurrentUser _currentUser;

    public PortalLinkController(IPortalService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("{slug}/portal/links")]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> Index()
    {
        var links = await _service.GetLinksAsync();
        return SwapView("Index", links);
    }

    [HttpGet("{slug}/portal/links/client/{clientId:guid}")]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> ClientLinks(Guid clientId)
    {
        var links = await _service.GetLinksAsync(clientId);
        return PartialView("_LinkList", links);
    }

    [HttpGet("{slug}/portal/links/new/{clientId:guid}")]
    [HasPermission(PortalPermissions.PortalManage)]
    public IActionResult New(Guid clientId)
    {
        var dto = new CreatePortalLinkDto { ClientId = clientId };
        return PartialView("_CreateForm", dto);
    }

    [HttpPost("{slug}/portal/links/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> Create([FromForm] CreatePortalLinkDto dto)
    {
        var userId = _currentUser.UserId ?? string.Empty;
        var link = await _service.CreateLinkAsync(dto, userId);
        var links = await _service.GetLinksAsync(dto.ClientId);
        return PartialView("_LinkList", links);
    }

    [HttpPost("{slug}/portal/links/revoke/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> Revoke(Guid id)
    {
        await _service.RevokeAsync(id);
        var links = await _service.GetLinksAsync();
        return PartialView("_LinkList", links);
    }
}
