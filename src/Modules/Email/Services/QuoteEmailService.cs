using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure.Services;
using saas.Modules.Auth.Services;
using saas.Modules.Branding.Entities;
using saas.Modules.Email.DTOs;
using saas.Modules.Email.Entities;
using saas.Modules.Quotes.Entities;
using saas.Shared;

namespace saas.Modules.Email.Services;

public interface IQuoteEmailService
{
    Task<QuoteEmailComposeDto?> GetComposeAsync(Guid quoteId);
    Task<QuoteEmailComposeDto?> RehydrateComposeAsync(Guid quoteId, QuoteEmailComposeDto dto);
    Task<QuoteEmailHistoryDto?> GetHistoryAsync(Guid quoteId);
    Task<EmailSendResult> SendQuoteAsync(Guid quoteId, QuoteEmailComposeDto dto);
}

public class QuoteEmailService : IQuoteEmailService
{
    private readonly TenantDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateService _templateService;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;

    public QuoteEmailService(
        TenantDbContext db,
        IEmailService emailService,
        IEmailTemplateService templateService,
        ICurrentUser currentUser,
        ITenantContext tenantContext)
    {
        _db = db;
        _emailService = emailService;
        _templateService = templateService;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    public async Task<QuoteEmailComposeDto?> GetComposeAsync(Guid quoteId)
    {
        var snapshot = await GetQuoteSnapshotAsync(quoteId);
        if (snapshot is null)
        {
            return null;
        }

        return BuildComposeDto(snapshot, null);
    }

    public async Task<QuoteEmailComposeDto?> RehydrateComposeAsync(Guid quoteId, QuoteEmailComposeDto dto)
    {
        var snapshot = await GetQuoteSnapshotAsync(quoteId);
        if (snapshot is null)
        {
            return null;
        }

        return BuildComposeDto(snapshot, dto);
    }

    public async Task<QuoteEmailHistoryDto?> GetHistoryAsync(Guid quoteId)
    {
        var quote = await _db.Quotes.AsNoTracking()
            .Where(x => x.Id == quoteId)
            .Select(x => new { x.Id, x.ReferenceNumber })
            .FirstOrDefaultAsync();

        if (quote is null)
        {
            return null;
        }

        return new QuoteEmailHistoryDto
        {
            QuoteId = quote.Id,
            ReferenceNumber = quote.ReferenceNumber,
            Items = await _db.Set<QuoteEmailLog>()
                .AsNoTracking()
                .Where(x => x.QuoteId == quoteId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new QuoteEmailHistoryItemDto
                {
                    Id = x.Id,
                    ToEmail = x.ToEmail,
                    Subject = x.Subject,
                    CustomMessage = x.CustomMessage,
                    Status = x.Status,
                    ErrorMessage = x.ErrorMessage,
                    SentAt = x.SentAt,
                    SentByDisplayName = x.SentByDisplayName,
                    SentByEmail = x.SentByEmail
                })
                .ToListAsync()
        };
    }

    public async Task<EmailSendResult> SendQuoteAsync(Guid quoteId, QuoteEmailComposeDto dto)
    {
        var snapshot = await GetQuoteSnapshotAsync(quoteId);
        if (snapshot is null)
        {
            return EmailSendResult.Failed("Quote was not found.");
        }

        var toEmail = dto.ToEmail.Trim();
        var subject = dto.Subject.Trim();
        var customMessage = Normalize(dto.CustomMessage);

        var log = new QuoteEmailLog
        {
            QuoteId = snapshot.Quote.Id,
            ToEmail = toEmail,
            Subject = subject,
            CustomMessage = customMessage,
            Status = QuoteEmailDeliveryStatus.Pending,
            SentByDisplayName = _currentUser.DisplayName,
            SentByEmail = _currentUser.Email
        };

        _db.Set<QuoteEmailLog>().Add(log);
        await _db.SaveChangesAsync();

        var variables = BuildTemplateVariables(snapshot, customMessage);
        var htmlBody = _templateService.Render("QuoteShare", variables);
        var plainTextBody = BuildPlainText(snapshot, customMessage);

        var result = await _emailService.SendAsync(new EmailMessage(
            To: toEmail,
            Subject: subject,
            HtmlBody: htmlBody,
            PlainTextBody: plainTextBody));

        log.Status = result.Success ? QuoteEmailDeliveryStatus.Sent : QuoteEmailDeliveryStatus.Failed;
        log.ErrorMessage = result.ErrorMessage;
        log.SentAt = DateTime.UtcNow;

        if (result.Success && snapshot.Quote.Status == QuoteStatus.Draft)
        {
            snapshot.Quote.Status = QuoteStatus.Sent;
        }

        await _db.SaveChangesAsync();
        return result;
    }

    private async Task<QuoteEmailSnapshot?> GetQuoteSnapshotAsync(Guid quoteId)
    {
        var quote = await _db.Quotes
            .Include(x => x.QuoteRateCards)
                .ThenInclude(x => x.RateCard)
                    .ThenInclude(x => x!.InventoryItem)
            .FirstOrDefaultAsync(x => x.Id == quoteId);

        if (quote is null)
        {
            return null;
        }

        var branding = await _db.BrandingSettings.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync() ?? new BrandingSettings();
        return new QuoteEmailSnapshot(quote, branding, _tenantContext.TenantName ?? "Your workspace");
    }

    private QuoteEmailComposeDto BuildComposeDto(QuoteEmailSnapshot snapshot, QuoteEmailComposeDto? draft)
    {
        return new QuoteEmailComposeDto
        {
            QuoteId = snapshot.Quote.Id,
            ReferenceNumber = snapshot.Quote.ReferenceNumber,
            ClientName = snapshot.Quote.ClientName,
            ClientEmail = snapshot.Quote.ClientEmail,
            TravelWindowLabel = FormatTravelWindow(snapshot.Quote.TravelStartDate, snapshot.Quote.TravelEndDate),
            ValidUntilLabel = snapshot.Quote.ValidUntil?.ToString("dd MMM yyyy") ?? "Not set",
            RateCardCount = snapshot.Quote.QuoteRateCards.Count,
            AgencyName = GetAgencyName(snapshot.Branding, snapshot.FallbackAgencyName),
            ReplyToEmail = Normalize(snapshot.Branding.PublicContactEmail),
            ReplyToPhone = Normalize(snapshot.Branding.ContactPhone),
            ToEmail = draft?.ToEmail ?? snapshot.Quote.ClientEmail ?? string.Empty,
            Subject = draft?.Subject ?? BuildDefaultSubject(snapshot),
            CustomMessage = draft?.CustomMessage
        };
    }

    private Dictionary<string, string> BuildTemplateVariables(QuoteEmailSnapshot snapshot, string? customMessage)
    {
        var quote = snapshot.Quote;
        var branding = snapshot.Branding;
        var hotelNames = quote.QuoteRateCards
            .Select(x => x.RateCard?.InventoryItem?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var agencyName = GetAgencyName(branding, snapshot.FallbackAgencyName);
        var travelWindow = FormatTravelWindow(quote.TravelStartDate, quote.TravelEndDate);

        return new Dictionary<string, string>
        {
            ["AgencyName"] = agencyName,
            ["ClientName"] = quote.ClientName,
            ["ReferenceNumber"] = quote.ReferenceNumber,
            ["TravelWindow"] = travelWindow ?? "Open travel window",
            ["ValidUntil"] = quote.ValidUntil?.ToString("dd MMM yyyy") ?? "Not set",
            ["OutputCurrencyCode"] = quote.OutputCurrencyCode,
            ["MarkupPercentage"] = quote.MarkupPercentage.ToString("0.##"),
            ["RateCardCount"] = quote.QuoteRateCards.Count.ToString(),
            ["Hotels"] = hotelNames.Count == 0 ? "Selected supplier contracts" : string.Join(", ", hotelNames),
            ["CustomMessage"] = customMessage ?? "We have prepared your quote and would be happy to walk you through the options.",
            ["ClientNotes"] = Normalize(quote.Notes) ?? "No extra client-facing notes were included with this quote.",
            ["ContactEmail"] = Normalize(branding.PublicContactEmail) ?? "Reply to this email to reach our team.",
            ["ContactPhone"] = Normalize(branding.ContactPhone) ?? "Phone not provided",
            ["Website"] = Normalize(branding.Website) ?? "Website not provided",
            ["FooterText"] = Normalize(branding.PdfFooterText) ?? "Thank you for considering this itinerary."
        };
    }

    private string BuildPlainText(QuoteEmailSnapshot snapshot, string? customMessage)
    {
        var agencyName = GetAgencyName(snapshot.Branding, snapshot.FallbackAgencyName);
        var lines = new List<string>
        {
            $"{agencyName} has prepared quote {snapshot.Quote.ReferenceNumber} for {snapshot.Quote.ClientName}.",
            string.Empty,
            customMessage ?? "We have prepared your quote and would be happy to walk you through the options.",
            string.Empty,
            $"Travel window: {FormatTravelWindow(snapshot.Quote.TravelStartDate, snapshot.Quote.TravelEndDate) ?? "Open travel window"}",
            $"Valid until: {snapshot.Quote.ValidUntil?.ToString("dd MMM yyyy") ?? "Not set"}",
            $"Currency: {snapshot.Quote.OutputCurrencyCode}",
            $"Markup: {snapshot.Quote.MarkupPercentage:0.##}%",
            $"Included contracts: {snapshot.Quote.QuoteRateCards.Count}",
            string.Empty,
            Normalize(snapshot.Quote.Notes) ?? "No extra client-facing notes were included with this quote.",
            string.Empty,
            $"Contact email: {Normalize(snapshot.Branding.PublicContactEmail) ?? "Reply to this email"}",
            $"Contact phone: {Normalize(snapshot.Branding.ContactPhone) ?? "Not provided"}",
            $"Website: {Normalize(snapshot.Branding.Website) ?? "Not provided"}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildDefaultSubject(QuoteEmailSnapshot snapshot)
        => $"{GetAgencyName(snapshot.Branding, snapshot.FallbackAgencyName)} quote {snapshot.Quote.ReferenceNumber} for {snapshot.Quote.ClientName}";

    private static string GetAgencyName(BrandingSettings branding, string fallbackAgencyName)
        => string.IsNullOrWhiteSpace(branding.AgencyName) ? fallbackAgencyName : branding.AgencyName.Trim();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatTravelWindow(DateOnly? startDate, DateOnly? endDate)
    {
        if (!startDate.HasValue && !endDate.HasValue)
        {
            return null;
        }

        if (startDate.HasValue && endDate.HasValue)
        {
            return $"{startDate.Value:dd MMM yyyy} - {endDate.Value:dd MMM yyyy}";
        }

        return startDate.HasValue
            ? $"From {startDate.Value:dd MMM yyyy}"
            : $"Until {endDate!.Value:dd MMM yyyy}";
    }

    private sealed record QuoteEmailSnapshot(Quote Quote, BrandingSettings Branding, string FallbackAgencyName);
}
