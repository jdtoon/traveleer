using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Portal.Entities;
using saas.Modules.Portal.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Portal.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(PortalFeatures.Portal)]
public class ClientActionController : SwapController
{
    private readonly IClientActionService _service;
    private readonly ICurrentUser _currentUser;

    public ClientActionController(IClientActionService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("{slug}/portal/actions")]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> Index([FromQuery] ClientActionStatus? status)
    {
        var actions = await _service.GetActionsAsync(status);
        var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
        Breadcrumbs.Set(ViewData, "Client Actions", "Portal", $"/{slug}/portal/links");
        return SwapView("ActionIndex", actions);
    }

    [HttpGet("{slug}/portal/actions/list")]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> List([FromQuery] ClientActionStatus? status)
    {
        var actions = await _service.GetActionsAsync(status);
        return PartialView("_ActionList", actions);
    }

    [HttpPost("{slug}/portal/actions/acknowledge/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var userId = _currentUser.UserId ?? "system";
        await _service.AcknowledgeAsync(id, userId);
        var actions = await _service.GetActionsAsync();
        return SwapResponse()
            .WithView("_ActionList", actions)
            .WithSuccessToast("Action acknowledged.")
            .Build();
    }

    [HttpPost("{slug}/portal/actions/dismiss/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(PortalPermissions.PortalManage)]
    public async Task<IActionResult> Dismiss(Guid id)
    {
        var userId = _currentUser.UserId ?? "system";
        await _service.DismissAsync(id, userId);
        var actions = await _service.GetActionsAsync();
        return SwapResponse()
            .WithView("_ActionList", actions)
            .WithSuccessToast("Action dismissed.")
            .Build();
    }
}
