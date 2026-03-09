using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Events;
using saas.Modules.RateCards.Services;
using Swap.Htmx;

namespace saas.Modules.RateCards.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(RateCardFeatures.RateCards)]
[Route("{slug}/rate-cards")]
public class RateCardController : SwapController
{
    private readonly IRateCardService _service;

    public RateCardController(IRateCardService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public IActionResult Index([FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] string? search = null, [FromQuery] int page = 1)
    {
        ViewData["Status"] = status;
        ViewData["Search"] = search;
        var model = await _service.GetListAsync(status, search, page);
        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> New()
        => PartialView("_Form", await _service.CreateEmptyAsync());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> Create([FromForm] RateCardFormDto dto)
    {
        if (!ModelState.IsValid)
        {
            var empty = await _service.CreateEmptyAsync();
            dto.InventoryOptions = empty.InventoryOptions;
            dto.MealPlanOptions = empty.MealPlanOptions;
            dto.CurrencyOptions = empty.CurrencyOptions;
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_Form", dto)
                .Build();
        }

        try
        {
            var id = await _service.CreateAsync(dto);
            Response.Headers["HX-Redirect"] = Url.Action(nameof(Details), new { slug = RouteData.Values["slug"], id }) ?? string.Empty;
            return SwapResponse()
                .WithView("_ModalClose")
                .WithSuccessToast("Rate card created.")
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            var empty = await _service.CreateEmptyAsync();
            dto.InventoryOptions = empty.InventoryOptions;
            dto.MealPlanOptions = empty.MealPlanOptions;
            dto.CurrencyOptions = empty.CurrencyOptions;
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("_Form", dto)
                .Build();
        }
    }

    [HttpGet("details/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : SwapView(model);
    }

    [HttpGet("summary/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> Summary(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : PartialView("_Summary", model);
    }

    [HttpGet("grid/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> Grid(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : PartialView("_Grid", model);
    }

    [HttpGet("seasons/new/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> NewSeason(Guid id)
        => PartialView("_SeasonForm", await _service.CreateEmptySeasonAsync(id));

    [HttpGet("seasons/edit/{rateCardId:guid}/{seasonId:guid}")]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> EditSeason(Guid rateCardId, Guid seasonId)
    {
        var model = await _service.GetSeasonAsync(rateCardId, seasonId);
        return model is null ? NotFound() : PartialView("_SeasonForm", model);
    }

    [HttpPost("seasons/create/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> CreateSeason(Guid id, [FromForm] RateSeasonFormDto dto)
    {
        dto.RateCardId = id;
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_SeasonForm", dto)
                .Build();
        }

        try
        {
            await _service.CreateSeasonAsync(id, dto);
            return SwapResponse()
                .WithView("_ModalClose")
                .WithSuccessToast("Season added.")
                .WithTrigger(RateCardEvents.DetailsRefresh)
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("_SeasonForm", dto)
                .Build();
        }
    }

    [HttpPost("seasons/update/{rateCardId:guid}/{seasonId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> UpdateSeason(Guid rateCardId, Guid seasonId, [FromForm] RateSeasonFormDto dto)
    {
        dto.Id = seasonId;
        dto.RateCardId = rateCardId;
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_SeasonForm", dto)
                .Build();
        }

        try
        {
            await _service.UpdateSeasonAsync(rateCardId, dto);
            return SwapResponse()
                .WithView("_ModalClose")
                .WithSuccessToast("Season updated.")
                .WithTrigger(RateCardEvents.DetailsRefresh)
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("_SeasonForm", dto)
                .Build();
        }
    }

    [HttpGet("seasons/delete/{rateCardId:guid}/{seasonId:guid}")]
    [HasPermission(RateCardPermissions.RateCardsDelete)]
    public async Task<IActionResult> ConfirmDeleteSeason(Guid rateCardId, Guid seasonId)
    {
        var model = await _service.GetSeasonAsync(rateCardId, seasonId);
        return model is null ? NotFound() : PartialView("_DeleteSeasonConfirm", model);
    }

    [HttpPost("seasons/delete/{rateCardId:guid}/{seasonId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsDelete)]
    public async Task<IActionResult> DeleteSeason(Guid rateCardId, Guid seasonId)
    {
        await _service.DeleteSeasonAsync(rateCardId, seasonId);
        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Season deleted.")
            .WithTrigger(RateCardEvents.DetailsRefresh)
            .Build();
    }

    [HttpPost("rates/update")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> UpdateRate([FromForm] RateCardRateUpdateDto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please enter valid rates.")
                .Build();
        }

        await _service.UpdateRateAsync(dto);
        return SwapResponse()
            .WithSuccessToast("Rate updated.")
            .WithTrigger(RateCardEvents.DetailsRefresh)
            .Build();
    }

    [HttpPost("activate/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> Activate(Guid id)
    {
        try
        {
            await _service.ActivateAsync(id);
            return SwapResponse()
                .WithSuccessToast("Rate card activated.")
                .WithTrigger(RateCardEvents.DetailsRefresh)
                .WithTrigger(RateCardEvents.Refresh)
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse().WithErrorToast(ex.Message).Build();
        }
    }

    [HttpPost("archive/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> Archive(Guid id)
    {
        await _service.ArchiveAsync(id);
        return SwapResponse()
            .WithSuccessToast("Rate card archived.")
            .WithTrigger(RateCardEvents.DetailsRefresh)
            .WithTrigger(RateCardEvents.Refresh)
            .Build();
    }

    [HttpPost("draft/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> SetDraft(Guid id)
    {
        await _service.SetDraftAsync(id);
        return SwapResponse()
            .WithSuccessToast("Rate card moved to draft.")
            .WithTrigger(RateCardEvents.DetailsRefresh)
            .WithTrigger(RateCardEvents.Refresh)
            .Build();
    }

    [HttpPost("duplicate/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> Duplicate(Guid id)
    {
        var duplicateId = await _service.DuplicateAsync(id);
        Response.Headers["HX-Redirect"] = Url.Action(nameof(Details), new { slug = RouteData.Values["slug"], id = duplicateId }) ?? string.Empty;
        return SwapResponse()
            .WithSuccessToast("Rate card duplicated.")
            .Build();
    }
}
