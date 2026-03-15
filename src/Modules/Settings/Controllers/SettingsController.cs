using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Settings.DTOs;
using saas.Modules.Settings.Entities;
using saas.Modules.Settings.Events;
using saas.Modules.Settings.Services;
using saas.Modules.TenantAdmin.Services;
using Swap.Htmx;

namespace saas.Modules.Settings.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(SettingsFeatures.Settings)]
[Route("{slug}/settings")]
public class SettingsController : SwapController
{
    private static readonly HashSet<string> ValidTabs =
    [
        "room-types",
        "meal-plans",
        "currencies",
        "destinations",
        "suppliers",
        "rate-categories",
        "users"
    ];

    private readonly ISettingsService _service;
    private readonly ITenantAdminService _tenantAdminService;

    public SettingsController(ISettingsService service, ITenantAdminService tenantAdminService)
    {
        _service = service;
        _tenantAdminService = tenantAdminService;
    }

    [HttpGet("")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public IActionResult Index([FromQuery] string? tab = null)
    {
        var activeTab = !string.IsNullOrWhiteSpace(tab) && ValidTabs.Contains(tab)
            ? tab
            : "room-types";

        ViewData["ActiveTab"] = activeTab;
        return SwapView();
    }

    [HttpGet("room-types")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> RoomTypes()
        => PartialView("_RoomTypesList", await _service.GetRoomTypesAsync());

    [HttpGet("room-types/new")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> NewRoomType()
        => PartialView("_RoomTypeForm", await _service.CreateEmptyRoomTypeAsync());

    [HttpGet("room-types/edit/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> EditRoomType(Guid id)
    {
        var model = await _service.GetRoomTypeAsync(id);
        return model is null ? NotFound() : PartialView("_RoomTypeForm", model);
    }

    [HttpPost("room-types/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> CreateRoomType([FromForm] RoomTypeDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidForm("_RoomTypeForm", dto);

        if (await _service.RoomTypeCodeExistsAsync(dto.Code))
        {
            ModelState.AddModelError(nameof(RoomTypeDto.Code), "Another room type with this code already exists.");
            return DuplicateForm("_RoomTypeForm", dto, "Another room type with this code already exists.");
        }

        await _service.CreateRoomTypeAsync(dto);
        return SuccessResponse("Room type created.", SettingsEvents.RoomTypesRefresh);
    }

    [HttpPost("room-types/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateRoomType(Guid id, [FromForm] RoomTypeDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
            return InvalidForm("_RoomTypeForm", dto);

        if (await _service.RoomTypeCodeExistsAsync(dto.Code, id))
        {
            ModelState.AddModelError(nameof(RoomTypeDto.Code), "Another room type with this code already exists.");
            return DuplicateForm("_RoomTypeForm", dto, "Another room type with this code already exists.");
        }

        await _service.UpdateRoomTypeAsync(id, dto);
        return SuccessResponse("Room type updated.", SettingsEvents.RoomTypesRefresh);
    }

    [HttpGet("room-types/delete-confirm/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteRoomTypeConfirm(Guid id)
    {
        var item = await _service.GetRoomTypeAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", BuildDeleteModel($"Delete {item.Name}?", "This removes the room type from this tenant.", Url.Action("DeleteRoomType", new { slug = RouteData.Values["slug"], id })!));
    }

    [HttpPost("room-types/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteRoomType(Guid id)
    {
        await _service.DeleteRoomTypeAsync(id);
        return SuccessResponse("Room type deleted.", SettingsEvents.RoomTypesRefresh);
    }

    [HttpGet("meal-plans")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> MealPlans()
        => PartialView("_MealPlansList", await _service.GetMealPlansAsync());

    [HttpGet("meal-plans/new")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> NewMealPlan()
        => PartialView("_MealPlanForm", await _service.CreateEmptyMealPlanAsync());

    [HttpGet("meal-plans/edit/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> EditMealPlan(Guid id)
    {
        var model = await _service.GetMealPlanAsync(id);
        return model is null ? NotFound() : PartialView("_MealPlanForm", model);
    }

    [HttpPost("meal-plans/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> CreateMealPlan([FromForm] MealPlanDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidForm("_MealPlanForm", dto);
        if (await _service.MealPlanCodeExistsAsync(dto.Code))
        {
            ModelState.AddModelError(nameof(MealPlanDto.Code), "Another meal plan with this code already exists.");
            return DuplicateForm("_MealPlanForm", dto, "Another meal plan with this code already exists.");
        }
        await _service.CreateMealPlanAsync(dto);
        return SuccessResponse("Meal plan created.", SettingsEvents.MealPlansRefresh);
    }

    [HttpPost("meal-plans/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateMealPlan(Guid id, [FromForm] MealPlanDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
            return InvalidForm("_MealPlanForm", dto);
        if (await _service.MealPlanCodeExistsAsync(dto.Code, id))
        {
            ModelState.AddModelError(nameof(MealPlanDto.Code), "Another meal plan with this code already exists.");
            return DuplicateForm("_MealPlanForm", dto, "Another meal plan with this code already exists.");
        }
        await _service.UpdateMealPlanAsync(id, dto);
        return SuccessResponse("Meal plan updated.", SettingsEvents.MealPlansRefresh);
    }

    [HttpGet("meal-plans/delete-confirm/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteMealPlanConfirm(Guid id)
    {
        var item = await _service.GetMealPlanAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", BuildDeleteModel($"Delete {item.Name}?", "This removes the meal plan from this tenant.", Url.Action("DeleteMealPlan", new { slug = RouteData.Values["slug"], id })!));
    }

    [HttpPost("meal-plans/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteMealPlan(Guid id)
    {
        await _service.DeleteMealPlanAsync(id);
        return SuccessResponse("Meal plan deleted.", SettingsEvents.MealPlansRefresh);
    }

    [HttpGet("currencies")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> Currencies()
        => PartialView("_CurrenciesList", await _service.GetCurrenciesAsync());

    [HttpGet("currencies/new")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> NewCurrency()
        => PartialView("_CurrencyForm", await _service.CreateEmptyCurrencyAsync());

    [HttpGet("currencies/edit/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> EditCurrency(Guid id)
    {
        var model = await _service.GetCurrencyAsync(id);
        return model is null ? NotFound() : PartialView("_CurrencyForm", model);
    }

    [HttpPost("currencies/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> CreateCurrency([FromForm] CurrencyDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidForm("_CurrencyForm", dto);
        if (await _service.CurrencyCodeExistsAsync(dto.Code))
        {
            ModelState.AddModelError(nameof(CurrencyDto.Code), "Another currency with this code already exists.");
            return DuplicateForm("_CurrencyForm", dto, "Another currency with this code already exists.");
        }
        await _service.CreateCurrencyAsync(dto);
        return SuccessResponse("Currency created.", SettingsEvents.CurrenciesRefresh);
    }

    [HttpPost("currencies/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateCurrency(Guid id, [FromForm] CurrencyDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
            return InvalidForm("_CurrencyForm", dto);
        if (await _service.CurrencyCodeExistsAsync(dto.Code, id))
        {
            ModelState.AddModelError(nameof(CurrencyDto.Code), "Another currency with this code already exists.");
            return DuplicateForm("_CurrencyForm", dto, "Another currency with this code already exists.");
        }
        await _service.UpdateCurrencyAsync(id, dto);
        return SuccessResponse("Currency updated.", SettingsEvents.CurrenciesRefresh);
    }

    [HttpGet("currencies/delete-confirm/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteCurrencyConfirm(Guid id)
    {
        var item = await _service.GetCurrencyAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", BuildDeleteModel($"Delete {item.Name}?", "This removes the currency from this tenant. Base currency cannot be deleted.", Url.Action("DeleteCurrency", new { slug = RouteData.Values["slug"], id })!));
    }

    [HttpPost("currencies/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteCurrency(Guid id)
    {
        try
        {
            await _service.DeleteCurrencyAsync(id);
            return SuccessResponse("Currency deleted.", SettingsEvents.CurrenciesRefresh);
        }
        catch (InvalidOperationException ex)
        {
            return SwapResponse().WithErrorToast(ex.Message).Build();
        }
    }

    [HttpPost("currencies/set-base/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> SetBaseCurrency(Guid id)
    {
        await _service.SetBaseCurrencyAsync(id);
        return SwapResponse()
            .WithSuccessToast("Base currency updated.")
            .WithTrigger(SettingsEvents.CurrenciesRefresh)
            .Build();
    }

    [HttpGet("destinations")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> Destinations()
        => PartialView("_DestinationsList", await _service.GetDestinationsAsync());

    [HttpGet("destinations/new")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> NewDestination()
        => PartialView("_DestinationForm", await _service.CreateEmptyDestinationAsync());

    [HttpGet("destinations/edit/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> EditDestination(Guid id)
    {
        var model = await _service.GetDestinationAsync(id);
        return model is null ? NotFound() : PartialView("_DestinationForm", model);
    }

    [HttpPost("destinations/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> CreateDestination([FromForm] DestinationDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidForm("_DestinationForm", dto);
        await _service.CreateDestinationAsync(dto);
        return SuccessResponse("Destination created.", SettingsEvents.DestinationsRefresh);
    }

    [HttpPost("destinations/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateDestination(Guid id, [FromForm] DestinationDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
            return InvalidForm("_DestinationForm", dto);
        await _service.UpdateDestinationAsync(id, dto);
        return SuccessResponse("Destination updated.", SettingsEvents.DestinationsRefresh);
    }

    [HttpGet("destinations/delete-confirm/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteDestinationConfirm(Guid id)
    {
        var item = await _service.GetDestinationAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", BuildDeleteModel($"Delete {item.Name}?", "This removes the destination from this tenant.", Url.Action("DeleteDestination", new { slug = RouteData.Values["slug"], id })!));
    }

    [HttpPost("destinations/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteDestination(Guid id)
    {
        await _service.DeleteDestinationAsync(id);
        return SuccessResponse("Destination deleted.", SettingsEvents.DestinationsRefresh);
    }

    [HttpGet("suppliers")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> Suppliers()
        => PartialView("_SuppliersList", await _service.GetSuppliersAsync());

    [HttpGet("suppliers/new")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> NewSupplier()
        => PartialView("_SupplierForm", await _service.CreateEmptySupplierAsync());

    [HttpGet("suppliers/edit/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> EditSupplier(Guid id)
    {
        var model = await _service.GetSupplierAsync(id);
        return model is null ? NotFound() : PartialView("_SupplierForm", model);
    }

    [HttpPost("suppliers/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> CreateSupplier([FromForm] SupplierDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidForm("_SupplierForm", dto);
        await _service.CreateSupplierAsync(dto);
        return SuccessResponse("Supplier created.", SettingsEvents.SuppliersRefresh);
    }

    [HttpPost("suppliers/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateSupplier(Guid id, [FromForm] SupplierDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
            return InvalidForm("_SupplierForm", dto);
        await _service.UpdateSupplierAsync(id, dto);
        return SuccessResponse("Supplier updated.", SettingsEvents.SuppliersRefresh);
    }

    [HttpGet("suppliers/delete-confirm/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteSupplierConfirm(Guid id)
    {
        var item = await _service.GetSupplierAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", BuildDeleteModel($"Delete {item.Name}?", "This removes the supplier from this tenant.", Url.Action("DeleteSupplier", new { slug = RouteData.Values["slug"], id })!));
    }

    [HttpPost("suppliers/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteSupplier(Guid id)
    {
        await _service.DeleteSupplierAsync(id);
        return SuccessResponse("Supplier deleted.", SettingsEvents.SuppliersRefresh);
    }

    [HttpGet("rate-categories")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> RateCategories()
        => PartialView("_RateCategoriesList", await _service.GetRateCategoryGroupsAsync());

    [HttpGet("rate-categories/new")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> NewRateCategory()
        => PartialView("_RateCategoryForm", await _service.CreateEmptyRateCategoryAsync());

    [HttpGet("rate-categories/edit/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> EditRateCategory(Guid id)
    {
        var model = await _service.GetRateCategoryAsync(id);
        return model is null ? NotFound() : PartialView("_RateCategoryForm", model);
    }

    [HttpPost("rate-categories/create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> CreateRateCategory([FromForm] RateCategoryDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidForm("_RateCategoryForm", dto);
        if (await _service.RateCategoryCodeExistsAsync(dto.ForType, dto.Code))
        {
            ModelState.AddModelError(nameof(RateCategoryDto.Code), "Another rate category with this type and code already exists.");
            return DuplicateForm("_RateCategoryForm", dto, "Another rate category with this type and code already exists.");
        }
        await _service.CreateRateCategoryAsync(dto);
        return SuccessResponse("Rate category created.", SettingsEvents.RateCategoriesRefresh);
    }

    [HttpPost("rate-categories/update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> UpdateRateCategory(Guid id, [FromForm] RateCategoryDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
            return InvalidForm("_RateCategoryForm", dto);
        if (await _service.RateCategoryCodeExistsAsync(dto.ForType, dto.Code, id))
        {
            ModelState.AddModelError(nameof(RateCategoryDto.Code), "Another rate category with this type and code already exists.");
            return DuplicateForm("_RateCategoryForm", dto, "Another rate category with this type and code already exists.");
        }
        await _service.UpdateRateCategoryAsync(id, dto);
        return SuccessResponse("Rate category updated.", SettingsEvents.RateCategoriesRefresh);
    }

    [HttpGet("rate-categories/delete-confirm/{id:guid}")]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteRateCategoryConfirm(Guid id)
    {
        var item = await _service.GetRateCategoryAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", BuildDeleteModel($"Delete {item.Name}?", "This removes the rate category from this tenant.", Url.Action("DeleteRateCategory", new { slug = RouteData.Values["slug"], id })!));
    }

    [HttpPost("rate-categories/delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SettingsPermissions.SettingsEdit)]
    public async Task<IActionResult> DeleteRateCategory(Guid id)
    {
        await _service.DeleteRateCategoryAsync(id);
        return SuccessResponse("Rate category deleted.", SettingsEvents.RateCategoriesRefresh);
    }

    [HttpGet("users")]
    [HasPermission(SettingsPermissions.SettingsRead)]
    public async Task<IActionResult> Users()
    {
        var users = await _tenantAdminService.GetUsersAsync(page: 1, pageSize: 50);
        return PartialView("_UsersList", new SettingsUsersViewModel { Users = users.Items });
    }

    private IActionResult InvalidForm(string viewName, object model)
        => SwapResponse().WithErrorToast("Please fix the errors below.").WithView(viewName, model).Build();

    private IActionResult DuplicateForm(string viewName, object model, string message)
        => SwapResponse().WithErrorToast(message).WithView(viewName, model).Build();

    private IActionResult SuccessResponse(string message, string trigger)
        => SwapResponse().WithView("_ModalClose").WithSuccessToast(message).WithTrigger(trigger).Build();

    private static SettingsDeleteConfirmDto BuildDeleteModel(string title, string message, string deleteUrl)
        => new()
        {
            Title = title,
            Message = message,
            DeleteUrl = deleteUrl
        };
}
