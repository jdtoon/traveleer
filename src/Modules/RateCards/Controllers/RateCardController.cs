using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using saas.Modules.Auth.Filters;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Events;
using saas.Modules.RateCards.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.RateCards.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(RateCardFeatures.RateCards)]
[Route("{slug}/rate-cards")]
public class RateCardController : SwapController
{
    private readonly IRateCardService _service;
    private readonly IRateCardTemplateService _templateService;
    private readonly IRateCardImportExportService _importExportService;

    public RateCardController(IRateCardService service, IRateCardTemplateService templateService, IRateCardImportExportService importExportService)
    {
        _service = service;
        _templateService = templateService;
        _importExportService = importExportService;
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
            var empty = await _service.CreateEmptyAsync(dto.InventoryItemId);
            dto.InventoryOptions = empty.InventoryOptions;
            dto.TemplateOptions = empty.TemplateOptions;
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
            var empty = await _service.CreateEmptyAsync(dto.InventoryItemId);
            dto.InventoryOptions = empty.InventoryOptions;
            dto.TemplateOptions = empty.TemplateOptions;
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
        if (model is not null)
        {
            var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
            Breadcrumbs.Set(ViewData, model.Name, "Rate Cards", $"/{slug}/rate-cards");
        }
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

    [HttpGet("templates/save/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> SaveAsTemplate(Guid id)
    {
        var details = await _service.GetDetailsAsync(id);
        if (details is null)
        {
            return NotFound();
        }

        return PartialView("_SaveAsTemplateForm", new SaveRateCardTemplateDto
        {
            RateCardId = id,
            Name = $"{details.Name} Template"
        });
    }

    [HttpPost("templates/save/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> SaveTemplate(Guid id, [FromForm] SaveRateCardTemplateDto dto)
    {
        dto.RateCardId = id;
        if (!ModelState.IsValid)
        {
            return SwapResponse()
                .WithErrorToast("Please fix the errors below.")
                .WithView("_SaveAsTemplateForm", dto)
                .Build();
        }

        try
        {
            await _templateService.CreateFromRateCardAsync(id, dto.Name, dto.Description);
            return SwapResponse()
                .WithView("_ModalClose")
                .WithSuccessToast("Template saved.")
                .WithTrigger(RateCardEvents.DetailsRefresh)
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse()
                .WithErrorToast(ex.Message)
                .WithView("_SaveAsTemplateForm", dto)
                .Build();
        }
    }

    [HttpGet("export/json/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> ExportJson(Guid id)
    {
        var export = await _importExportService.ExportJsonAsync(id, User.Identity?.Name);
        var fileName = $"ratecard-{SanitizeFileName(export.RateCard.Name)}-{DateTime.UtcNow:yyyyMMdd}.json";
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    [HttpGet("export/json")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> ExportAllJson()
    {
        var export = await _importExportService.ExportAllJsonAsync(User.Identity?.Name);
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"ratecards-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    [HttpGet("export/csv/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsRead)]
    public async Task<IActionResult> ExportCsv(Guid id)
    {
        var details = await _service.GetDetailsAsync(id);
        if (details is null)
        {
            return NotFound();
        }

        var csv = await _importExportService.ExportCsvAsync(id);
        var fileName = $"ratecard-{SanitizeFileName(details.Name)}-{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
    }

    [HttpGet("import/csv/{id:guid}")]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> ImportCsv(Guid id)
    {
        var details = await _service.GetDetailsAsync(id);
        if (details is null)
        {
            return NotFound();
        }

        return PartialView("_ImportCsvForm", new RateCardCsvImportFormDto
        {
            RateCardId = id,
            RateCardName = details.Name
        });
    }

    [HttpGet("import/json")]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public IActionResult ImportJson()
        => PartialView("_ImportJsonForm");

    [HttpPost("import/json/preview")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> PreviewImportJson(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return SwapResponse().WithErrorToast("Choose a JSON file to preview.").Build();
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var jsonContent = await reader.ReadToEndAsync();
            var preview = await _importExportService.PreviewJsonImportAsync(jsonContent);
            return PartialView("_ImportJsonPreview", preview);
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse().WithErrorToast(ex.Message).Build();
        }
    }

    [HttpPost("import/json/execute")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsCreate)]
    public async Task<IActionResult> ExecuteImportJson([FromForm] RateCardJsonImportExecuteDto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse().WithErrorToast("Import session expired. Upload the JSON again.").Build();
        }

        try
        {
            var result = await _importExportService.ExecuteJsonImportAsync(dto.ImportToken, dto.Actions);
            return SwapResponse()
                .WithView("_ModalClose")
                .WithSuccessToast($"Imported {result.ImportedCount} rate card{(result.ImportedCount == 1 ? string.Empty : "s")}." + (result.SkippedCount > 0 ? $" Skipped {result.SkippedCount}." : string.Empty))
                .WithTrigger(RateCardEvents.Refresh)
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse().WithErrorToast(ex.Message).Build();
        }
    }

    [HttpPost("import/csv/preview/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> PreviewImportCsv(Guid id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return SwapResponse().WithErrorToast("Choose a CSV file to preview.").Build();
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var csvContent = await reader.ReadToEndAsync();
            var preview = await _importExportService.PreviewCsvImportAsync(id, csvContent);
            return PartialView("_ImportCsvPreview", preview);
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse().WithErrorToast(ex.Message).Build();
        }
    }

    [HttpPost("import/csv/execute/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(RateCardPermissions.RateCardsEdit)]
    public async Task<IActionResult> ExecuteImportCsv(Guid id, [FromForm] RateCardCsvImportExecuteDto dto)
    {
        if (!ModelState.IsValid)
        {
            return SwapResponse().WithErrorToast("Import session expired. Upload the CSV again.").Build();
        }

        try
        {
            var result = await _importExportService.ExecuteCsvImportAsync(id, dto.ImportToken);
            return SwapResponse()
                .WithView("_ModalClose")
                .WithSuccessToast($"Imported {result.ImportedRowCount} rate update{(result.ImportedRowCount == 1 ? string.Empty : "s") }.")
                .WithTrigger(RateCardEvents.DetailsRefresh)
                .Build();
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse().WithErrorToast(ex.Message).Build();
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "ratecard" : sanitized;
    }
}
