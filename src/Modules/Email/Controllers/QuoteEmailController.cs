using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using saas.Modules.Auth.Filters;
using saas.Modules.Email.DTOs;
using saas.Modules.Email.Events;
using saas.Modules.Email.Services;
using saas.Modules.Quotes.Events;
using Swap.Htmx;

namespace saas.Modules.Email.Controllers;

[Authorize(Policy = "TenantUser")]
[RequireFeature(EmailFeatures.Email)]
[Route("{slug}/quote-email")]
public class QuoteEmailController : SwapController
{
    private readonly IQuoteEmailService _service;

    public QuoteEmailController(IQuoteEmailService service)
    {
        _service = service;
    }

    [HttpGet("compose/{quoteId:guid}")]
    [HasPermission(EmailPermissions.EmailSend)]
    public async Task<IActionResult> Compose(Guid quoteId)
    {
        var model = await _service.GetComposeAsync(quoteId);
        return model is null ? NotFound() : PartialView("_EmailModal", model);
    }

    [HttpPost("send/{quoteId:guid}")]
    [ValidateAntiForgeryToken]
    [HasPermission(EmailPermissions.EmailSend)]
    public async Task<IActionResult> Send(Guid quoteId, [FromForm] QuoteEmailComposeDto dto)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await _service.RehydrateComposeAsync(quoteId, dto);
            return invalidModel is null
                ? NotFound()
                : SwapResponse()
                    .WithErrorToast("Please fix the errors below.")
                    .WithView("_EmailModal", invalidModel)
                    .Build();
        }

        var result = await _service.SendQuoteAsync(quoteId, dto);
        if (!result.Success)
        {
            var retryModel = await _service.RehydrateComposeAsync(quoteId, dto);
            return retryModel is null
                ? NotFound()
                : SwapResponse()
                    .WithErrorToast(result.ErrorMessage ?? "Email could not be sent.")
                    .WithView("_EmailModal", retryModel)
                    .Build();
        }

        return SwapResponse()
            .WithView("_ModalClose")
            .WithSuccessToast("Quote email sent.")
            .WithTrigger(EmailEvents.Refresh)
            .WithTrigger(QuoteEvents.DetailsRefresh)
            .WithTrigger(QuoteEvents.Refresh)
            .Build();
    }

    [HttpGet("history/{quoteId:guid}")]
    [HasPermission(EmailPermissions.EmailRead)]
    public async Task<IActionResult> History(Guid quoteId)
    {
        var model = await _service.GetHistoryAsync(quoteId);
        return model is null ? NotFound() : PartialView("_EmailHistory", model);
    }
}
