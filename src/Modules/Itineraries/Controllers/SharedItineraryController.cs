using Microsoft.AspNetCore.Mvc;
using saas.Modules.Itineraries.Services;
using Swap.Htmx;

namespace saas.Modules.Itineraries.Controllers;

[Route("shared/itinerary")]
public class SharedItineraryController : SwapController
{
    private readonly IItineraryService _service;

    public SharedItineraryController(IItineraryService service)
    {
        _service = service;
    }

    [HttpGet("{token}")]
    public new async Task<IActionResult> View(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound();

        var model = await _service.GetByShareTokenAsync(token);
        return model is null ? NotFound() : SwapView("SharedView", model);
    }
}
