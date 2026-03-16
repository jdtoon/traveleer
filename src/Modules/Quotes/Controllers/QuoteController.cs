using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Quotes.DTOs;
using saas.Modules.Quotes.Entities;
using saas.Modules.Quotes.Events;
using saas.Modules.Quotes.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Quotes.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(QuoteFeatures.Quotes)]
[Route("{slug}/quotes")]
public class QuoteController : SwapController
{
    private readonly IQuoteService _service;

    public QuoteController(IQuoteService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public IActionResult Index([FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] string? search = null, [FromQuery] int page = 1)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        var model = await _service.GetListAsync(status, search, page);
        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(QuotePermissions.QuotesCreate)]
    public async Task<IActionResult> New()
    {
        var model = await _service.CreateEmptyAsync();
        var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
        Breadcrumbs.Set(ViewData, "New Quote", "Quotes", $"/{slug}/quotes");
        ViewData["ReferenceNumberPreview"] = await HttpContext.RequestServices.GetRequiredService<IQuoteNumberingService>().PreviewNextReferenceAsync();
        return SwapView("Builder", model);
    }

    [HttpGet("edit/{id:guid}")]
    [HasPermission(QuotePermissions.QuotesEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await _service.GetEditAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
        Breadcrumbs.Set(ViewData, "Edit Quote", "Quotes", $"/{slug}/quotes");
        ViewData["ReferenceNumberPreview"] = "Saved quote";
        ViewData["QuoteId"] = id;
        return SwapView("Builder", model);
    }

    [HttpGet("preview")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> Preview([FromQuery] QuoteBuilderDto dto)
    {
        var model = await _service.BuildPreviewAsync(dto);
        return PartialView("_Preview", model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(QuotePermissions.QuotesCreate)]
    public async Task<IActionResult> Create([FromForm] QuoteBuilderDto dto)
    {
        if (dto.SelectedRateCardIds.Count == 0)
        {
            ModelState.AddModelError(nameof(QuoteBuilderDto.SelectedRateCardIds), "Select at least one rate card.");
        }

        if (dto.TravelStartDate.HasValue && dto.TravelEndDate.HasValue && dto.TravelEndDate.Value < dto.TravelStartDate.Value)
        {
            ModelState.AddModelError(nameof(QuoteBuilderDto.TravelEndDate), "Travel end date must be on or after the start date.");
        }

        if (!ModelState.IsValid)
        {
            await _service.PopulateOptionsAsync(dto);
            ViewData["ReferenceNumberPreview"] = await HttpContext.RequestServices.GetRequiredService<IQuoteNumberingService>().PreviewNextReferenceAsync();
            return SwapView("Builder", dto);
        }

        var id = await _service.CreateAsync(dto);
        return RedirectToAction(nameof(Details), new { slug = RouteData.Values["slug"], id });
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(QuotePermissions.QuotesEdit)]
    public async Task<IActionResult> Update(Guid id, [FromForm] QuoteBuilderDto dto)
    {
        if (dto.SelectedRateCardIds.Count == 0)
        {
            ModelState.AddModelError(nameof(QuoteBuilderDto.SelectedRateCardIds), "Select at least one rate card.");
        }

        if (dto.TravelStartDate.HasValue && dto.TravelEndDate.HasValue && dto.TravelEndDate.Value < dto.TravelStartDate.Value)
        {
            ModelState.AddModelError(nameof(QuoteBuilderDto.TravelEndDate), "Travel end date must be on or after the start date.");
        }

        if (!ModelState.IsValid)
        {
            await _service.PopulateOptionsAsync(dto);
            ViewData["ReferenceNumberPreview"] = "Saved quote";
            ViewData["QuoteId"] = id;
            return SwapView("Builder", dto);
        }

        await _service.UpdateAsync(id, dto);
        return RedirectToAction(nameof(Details), new { slug = RouteData.Values["slug"], id });
    }

    [HttpGet("details/{id:guid}")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        if (model is not null)
        {
            var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
            Breadcrumbs.Set(ViewData, model.ReferenceNumber, "Quotes", $"/{slug}/quotes");
        }
        return model is null ? NotFound() : SwapView(model);
    }

    [HttpGet("summary/{id:guid}")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> Summary(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : PartialView("_Summary", model);
    }

    [HttpGet("preview/{id:guid}")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> SavedPreview(Guid id)
    {
        var model = await _service.BuildPreviewAsync(id);
        return model is null ? NotFound() : PartialView("_Preview", model);
    }

    [HttpGet("versions/{id:guid}")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> Versions(Guid id)
    {
        var model = await _service.GetVersionHistoryAsync(id);
        return model is null ? NotFound() : PartialView("_VersionHistory", model);
    }

    [HttpGet("versions/compare/{id:guid}")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> CompareVersions(Guid id, [FromQuery] Guid v1, [FromQuery] Guid v2)
    {
        var model = await _service.CompareVersionsAsync(id, v1, v2);
        if (model is null)
        {
            return SwapResponse()
                .WithErrorToast("Could not load versions for comparison.")
                .Build();
        }

        return PartialView("_VersionCompare", model);
    }

    [HttpGet("versions/{id:guid}/{versionId:guid}")]
    [HasPermission(QuotePermissions.QuotesRead)]
    public async Task<IActionResult> VersionDetails(Guid id, Guid versionId)
    {
        var model = await _service.GetVersionDetailsAsync(id, versionId);
        return model is null ? NotFound() : PartialView("_VersionDetails", model);
    }

    [HttpPost("status/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(QuotePermissions.QuotesEdit)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromForm] QuoteStatus status)
    {
        await _service.UpdateStatusAsync(id, status);
        return SwapResponse()
            .WithSuccessToast("Quote status updated.")
            .WithTrigger(QuoteEvents.DetailsRefresh)
            .WithTrigger(QuoteEvents.Refresh)
            .Build();
    }
}
