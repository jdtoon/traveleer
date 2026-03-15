using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Suppliers.DTOs;
using saas.Modules.Suppliers.Events;
using saas.Modules.Suppliers.Services;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Suppliers.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(SupplierFeatures.Suppliers)]
[Route("{slug}/suppliers")]
public class SupplierController : SwapController
{
    private readonly ISupplierService _service;

    public SupplierController(ISupplierService service)
    {
        _service = service;
    }

    [HttpGet("")]
    [HasPermission(SupplierPermissions.SuppliersRead)]
    public IActionResult Index([FromQuery] string? search = null)
    {
        ViewData["Search"] = search;
        return SwapView();
    }

    [HttpGet("list")]
    [HasPermission(SupplierPermissions.SuppliersRead)]
    public async Task<IActionResult> List([FromQuery] string? search = null)
    {
        var model = await _service.GetListAsync(search);
        return PartialView("_List", model);
    }

    [HttpGet("new")]
    [HasPermission(SupplierPermissions.SuppliersCreate)]
    public async Task<IActionResult> New()
        => PartialView("_Form", await _service.CreateEmptyAsync());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [HasPermission(SupplierPermissions.SuppliersCreate)]
    public async Task<IActionResult> Create([FromForm] SupplierFormDto dto)
    {
        if (!ModelState.IsValid)
        {
            dto.CurrencyOptions = (await _service.CreateEmptyAsync()).CurrencyOptions;
            return InvalidForm("_Form", dto);
        }

        await _service.CreateAsync(dto);
        return SuccessResponse("Supplier created.", SupplierEvents.Refresh);
    }

    [HttpGet("details/{id:guid}")]
    [HasPermission(SupplierPermissions.SuppliersRead)]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        if (model is not null)
        {
            var slug = RouteData.Values["slug"]?.ToString() ?? string.Empty;
            Breadcrumbs.Set(ViewData, model.Name, "Suppliers", $"/{slug}/suppliers");
        }
        return model is null ? NotFound() : SwapView(model);
    }

    [HttpGet("summary/{id:guid}")]
    [HasPermission(SupplierPermissions.SuppliersRead)]
    public async Task<IActionResult> Summary(Guid id)
    {
        var model = await _service.GetDetailsAsync(id);
        return model is null ? NotFound() : PartialView("_Summary", model);
    }

    [HttpGet("edit/{id:guid}")]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await _service.GetFormAsync(id);
        return model is null ? NotFound() : PartialView("_Form", model);
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> Update(Guid id, [FromForm] SupplierFormDto dto)
    {
        dto.Id = id;
        if (!ModelState.IsValid)
        {
            dto.CurrencyOptions = (await _service.CreateEmptyAsync()).CurrencyOptions;
            return InvalidForm("_Form", dto);
        }

        await _service.UpdateAsync(id, dto);
        return SuccessResponse("Supplier updated.", SupplierEvents.DetailsRefresh);
    }

    [HttpGet("delete-confirm/{id:guid}")]
    [HasPermission(SupplierPermissions.SuppliersDelete)]
    public async Task<IActionResult> DeleteConfirm(Guid id)
    {
        var item = await _service.GetDetailsAsync(id);
        return item is null ? NotFound() : PartialView("_DeleteConfirm", new SupplierDeleteConfirmDto
        {
            Title = $"Delete {item.Name}?",
            Message = "This removes the supplier and all associated contacts from this tenant.",
            DeleteUrl = Url.Action("Delete", new { slug = RouteData.Values["slug"], id })!
        });
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SupplierPermissions.SuppliersDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        Response.Headers["HX-Redirect"] = Url.Action(nameof(Index), new { slug = RouteData.Values["slug"] }) ?? string.Empty;
        return SwapResponse()
            .WithSuccessToast("Supplier deleted.")
            .Build();
    }

    // ========== CONTACTS ==========

    [HttpGet("contacts/{id:guid}")]
    [HasPermission(SupplierPermissions.SuppliersRead)]
    public async Task<IActionResult> Contacts(Guid id)
    {
        var contacts = await _service.GetContactsAsync(id);
        ViewData["SupplierId"] = id;
        return PartialView("_ContactList", contacts);
    }

    [HttpGet("contacts/new/{supplierId:guid}")]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> NewContact(Guid supplierId)
        => PartialView("_ContactForm", await _service.CreateEmptyContactAsync(supplierId));

    [HttpGet("contacts/edit/{contactId:guid}")]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> EditContact(Guid contactId)
    {
        var model = await _service.GetContactFormAsync(contactId);
        return model is null ? NotFound() : PartialView("_ContactForm", model);
    }

    [HttpPost("contacts/create/{supplierId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> CreateContact(Guid supplierId, [FromForm] SupplierContactFormDto dto)
    {
        dto.SupplierId = supplierId;
        if (!ModelState.IsValid)
            return InvalidForm("_ContactForm", dto);

        await _service.CreateContactAsync(dto);
        return SuccessResponse("Contact added.", SupplierEvents.ContactsRefresh);
    }

    [HttpPost("contacts/update/{contactId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> UpdateContact(Guid contactId, [FromForm] SupplierContactFormDto dto)
    {
        dto.Id = contactId;
        if (!ModelState.IsValid)
            return InvalidForm("_ContactForm", dto);

        await _service.UpdateContactAsync(contactId, dto);
        return SuccessResponse("Contact updated.", SupplierEvents.ContactsRefresh);
    }

    [HttpGet("contacts/delete-confirm/{contactId:guid}")]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> DeleteContactConfirm(Guid contactId)
    {
        var contact = await _service.GetContactFormAsync(contactId);
        return contact is null ? NotFound() : PartialView("_DeleteConfirm", new SupplierDeleteConfirmDto
        {
            Title = $"Delete {contact.Name}?",
            Message = "This removes the contact from this supplier.",
            DeleteUrl = Url.Action("DeleteContact", new { slug = RouteData.Values["slug"], contactId })!
        });
    }

    [HttpPost("contacts/delete/{contactId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(SupplierPermissions.SuppliersEdit)]
    public async Task<IActionResult> DeleteContact(Guid contactId)
    {
        await _service.DeleteContactAsync(contactId);
        return SuccessResponse("Contact removed.", SupplierEvents.ContactsRefresh);
    }

    // ========== HELPERS ==========

    private IActionResult InvalidForm(string viewName, object model)
        => SwapResponse().WithErrorToast("Please fix the errors below.").WithView(viewName, model).Build();

    private IActionResult SuccessResponse(string message, string trigger)
        => SwapResponse().WithView("_ModalClose").WithSuccessToast(message).WithTrigger(trigger).Build();
}
