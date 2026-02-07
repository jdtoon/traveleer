using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swap.Htmx;
using Swap.Htmx.Events;
using saas.Modules.Auth.Filters;
using saas.Modules.Notes.Entities;
using saas.Modules.Notes.Events;
using saas.Modules.Notes.Services;
using saas.Shared;

namespace saas.Modules.Notes.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(FeatureDefinitions.Notes)]
public class NotesController : SwapController
{
    private readonly INotesService _service;

    public NotesController(INotesService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var notes = await _service.GetAllAsync();
        return SwapView(notes);
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var notes = await _service.GetAllAsync();
        return SwapView("_NotesList", notes);
    }

    [HttpGet]
    [HasPermission(PermissionDefinitions.NotesCreate)]
    public IActionResult Create()
    {
        return SwapView("_CreateModal");
    }

    [HttpPost]
    [HasPermission(PermissionDefinitions.NotesCreate)]
    public async Task<IActionResult> Create(Note note)
    {
        if (!ModelState.IsValid)
        {
            return SwapView("_CreateModal", note);
        }

        await _service.CreateAsync(note);

        return SwapResponse()
            .WithSuccessToast("Note created!")
            .WithTrigger(NotesEvents.Notes.ListChanged)
            .WithView("_ModalClose")
            .Build();
    }

    [HttpGet]
    [HasPermission(PermissionDefinitions.NotesEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var note = await _service.GetByIdAsync(id);
        if (note == null) return NotFound();

        return SwapView("_EditModal", note);
    }

    [HttpPost]
    [HasPermission(PermissionDefinitions.NotesEdit)]
    public async Task<IActionResult> Edit(Guid id, Note note)
    {
        if (!ModelState.IsValid)
        {
            return SwapView("_EditModal", note);
        }

        try
        {
            await _service.UpdateAsync(id, note);
            return SwapResponse()
                .WithSuccessToast("Note updated!")
                .WithTrigger(NotesEvents.Notes.ListChanged)
                .WithView("_ModalClose")
                .Build();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpGet]
    [HasPermission(PermissionDefinitions.NotesDelete)]
    public async Task<IActionResult> DeleteConfirm(Guid id)
    {
        var note = await _service.GetByIdAsync(id);
        if (note == null) return NotFound();

        return SwapView("_DeleteConfirmModal", note);
    }

    [HttpPost]
    [HasPermission(PermissionDefinitions.NotesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _service.DeleteAsync(id);
            return SwapResponse()
                .WithSuccessToast("Note deleted!")
                .WithTrigger(NotesEvents.Notes.ListChanged)
                .WithView("_ModalClose")
                .Build();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [HasPermission(PermissionDefinitions.NotesEdit)]
    public async Task<IActionResult> TogglePin(Guid id)
    {
        try
        {
            await _service.TogglePinAsync(id);
            return SwapResponse()
                .WithTrigger(NotesEvents.Notes.ListChanged)
                .Build();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}