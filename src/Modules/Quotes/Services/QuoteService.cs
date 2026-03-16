using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Branding.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Quotes.DTOs;
using saas.Modules.Quotes.Entities;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Quotes.Services;

public interface IQuoteService
{
    Task<PaginatedList<QuoteListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12, Guid? clientId = null);
    Task<QuoteBuilderDto> CreateEmptyAsync();
    Task<QuoteBuilderDto?> GetEditAsync(Guid id);
    Task PopulateOptionsAsync(QuoteBuilderDto dto);
    Task<QuotePreviewDto> BuildPreviewAsync(QuoteBuilderDto dto);
    Task<QuotePreviewDto?> BuildPreviewAsync(Guid id);
    Task<Guid> CreateAsync(QuoteBuilderDto dto);
    Task UpdateAsync(Guid id, QuoteBuilderDto dto);
    Task<QuoteDetailsDto?> GetDetailsAsync(Guid id);
    Task<QuoteVersionHistoryDto?> GetVersionHistoryAsync(Guid id);
    Task<QuoteVersionDetailsDto?> GetVersionDetailsAsync(Guid id, Guid versionId);
    Task UpdateStatusAsync(Guid id, QuoteStatus status);
}

public class QuoteService : IQuoteService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TenantDbContext _db;
    private readonly IQuoteNumberingService _numberingService;

    public QuoteService(TenantDbContext db, IQuoteNumberingService numberingService)
    {
        _db = db;
        _numberingService = numberingService;
    }

    public async Task<PaginatedList<QuoteListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12, Guid? clientId = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Quotes
            .AsNoTracking()
            .Include(x => x.QuoteRateCards)
                .ThenInclude(x => x.RateCard)
                    .ThenInclude(x => x!.InventoryItem)
            .AsQueryable();

        if (clientId.HasValue)
        {
            query = query.Where(x => x.ClientId == clientId.Value);
        }

        if (TryParseStatus(status, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.ReferenceNumber.ToLower().Contains(term) ||
                x.ClientName.ToLower().Contains(term) ||
                (x.ClientEmail != null && x.ClientEmail.ToLower().Contains(term)) ||
                x.QuoteRateCards.Any(qrc => qrc.RateCard != null && qrc.RateCard.InventoryItem != null && qrc.RateCard.InventoryItem.Name.ToLower().Contains(term)));
        }

        var projected = query
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Select(x => new QuoteListItemDto
            {
                Id = x.Id,
                ClientId = x.ClientId,
                ReferenceNumber = x.ReferenceNumber,
                Status = x.Status,
                ClientName = x.ClientName,
                ClientEmail = x.ClientEmail,
                OutputCurrencyCode = x.OutputCurrencyCode,
                RateCardCount = x.QuoteRateCards.Count,
                PrimaryHotelName = x.QuoteRateCards
                    .OrderBy(qrc => qrc.SortOrder)
                    .Select(qrc => qrc.RateCard != null && qrc.RateCard.InventoryItem != null ? qrc.RateCard.InventoryItem.Name : null)
                    .FirstOrDefault(),
                TravelStartDate = x.TravelStartDate,
                TravelEndDate = x.TravelEndDate,
                UpdatedAt = x.UpdatedAt ?? x.CreatedAt
            });

        return await PaginatedList<QuoteListItemDto>.CreateAsync(projected, page, pageSize);
    }

    public async Task<QuoteBuilderDto> CreateEmptyAsync()
    {
        var baseCurrency = await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(x => x.IsBaseCurrency && x.IsActive);
        var branding = await _db.BrandingSettings.AsNoTracking().FirstOrDefaultAsync();
        var dto = new QuoteBuilderDto
        {
            OutputCurrencyCode = baseCurrency?.Code ?? "USD",
            MarkupPercentage = branding?.DefaultQuoteMarkupPercentage ?? baseCurrency?.DefaultMarkup ?? 10m,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(Math.Max(1, branding?.DefaultQuoteValidityDays ?? 14)))
        };

        await PopulateOptionsAsync(dto);
        return dto;
    }

    public async Task<QuoteBuilderDto?> GetEditAsync(Guid id)
    {
        var quote = await _db.Quotes
            .AsNoTracking()
            .Include(x => x.QuoteRateCards)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (quote is null)
        {
            return null;
        }

        var selectedRateCardIds = quote.QuoteRateCards.OrderBy(x => x.SortOrder).Select(x => x.RateCardId).ToList();
        var dto = new QuoteBuilderDto
        {
            TemplateLayout = quote.TemplateLayout,
            ShowImages = quote.ShowImages,
            ShowMealPlan = quote.ShowMealPlan,
            ShowFooter = quote.ShowFooter,
            ShowRoomDescriptions = quote.ShowRoomDescriptions,
            ClientId = quote.ClientId,
            ClientName = quote.ClientName,
            ClientEmail = quote.ClientEmail,
            ClientPhone = quote.ClientPhone,
            OutputCurrencyCode = quote.OutputCurrencyCode,
            MarkupPercentage = quote.MarkupPercentage,
            GroupBy = quote.GroupBy,
            ValidUntil = quote.ValidUntil,
            TravelStartDate = quote.TravelStartDate,
            TravelEndDate = quote.TravelEndDate,
            FilterByTravelDates = quote.FilterByTravelDates,
            Notes = quote.Notes,
            InternalNotes = quote.InternalNotes,
            SelectedRateCardIds = selectedRateCardIds
        };

        await PopulateOptionsAsync(dto);
        return dto;
    }

    public async Task PopulateOptionsAsync(QuoteBuilderDto dto)
    {
        dto.ClientOptions = await GetClientOptionsAsync();
        dto.CurrencyOptions = await GetCurrencyOptionsAsync();
        dto.AvailableRateCards = await GetAvailableRateCardsAsync(dto.SelectedRateCardIds);
    }

    public async Task<QuotePreviewDto> BuildPreviewAsync(QuoteBuilderDto dto)
    {
        await HydrateClientSnapshotAsync(dto);

        var currencies = await _db.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync();

        var outputCurrency = currencies.FirstOrDefault(x => string.Equals(x.Code, dto.OutputCurrencyCode, StringComparison.OrdinalIgnoreCase));
        var baseCurrency = currencies.FirstOrDefault(x => x.IsBaseCurrency) ?? outputCurrency;
        var selectedIds = dto.SelectedRateCardIds.Distinct().ToList();

        if (selectedIds.Count == 0)
        {
            var branding = await _db.BrandingSettings.AsNoTracking().FirstOrDefaultAsync();
            return new QuotePreviewDto
            {
                ClientName = dto.ClientName,
                OutputCurrencyCode = dto.OutputCurrencyCode,
                CurrencySymbol = outputCurrency?.Symbol ?? dto.OutputCurrencyCode,
                MarkupPercentage = dto.MarkupPercentage,
                TemplateLayout = NormalizeTemplateLayout(dto.TemplateLayout),
                ShowImages = dto.ShowImages,
                ShowMealPlan = dto.ShowMealPlan,
                ShowFooter = dto.ShowFooter,
                ShowRoomDescriptions = dto.ShowRoomDescriptions,
                FooterText = dto.ShowFooter ? branding?.PdfFooterText : null,
                FilterByTravelDates = dto.FilterByTravelDates,
                TravelStartDate = dto.TravelStartDate,
                TravelEndDate = dto.TravelEndDate
            };
        }

        var rateCards = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
                .ThenInclude(x => x!.Destination)
            .Include(x => x.DefaultMealPlan)
            .Include(x => x.Seasons.OrderBy(s => s.SortOrder))
                .ThenInclude(x => x.Rates)
                    .ThenInclude(x => x.RoomType)
            .Where(x => selectedIds.Contains(x.Id))
            .ToListAsync();

        var orderedCards = selectedIds
            .Select(id => rateCards.FirstOrDefault(x => x.Id == id))
            .Where(x => x is not null)
            .Cast<RateCard>()
            .ToList();

        var preview = new QuotePreviewDto
        {
            ClientName = dto.ClientName,
            OutputCurrencyCode = outputCurrency?.Code ?? dto.OutputCurrencyCode,
            CurrencySymbol = outputCurrency?.Symbol ?? outputCurrency?.Code ?? dto.OutputCurrencyCode,
            MarkupPercentage = dto.MarkupPercentage,
            TemplateLayout = NormalizeTemplateLayout(dto.TemplateLayout),
            ShowImages = dto.ShowImages,
            ShowMealPlan = dto.ShowMealPlan,
            ShowFooter = dto.ShowFooter,
            ShowRoomDescriptions = dto.ShowRoomDescriptions,
            FooterText = dto.ShowFooter ? await _db.BrandingSettings.AsNoTracking().Select(x => x.PdfFooterText).FirstOrDefaultAsync() : null,
            FilterByTravelDates = dto.FilterByTravelDates,
            TravelStartDate = dto.TravelStartDate,
            TravelEndDate = dto.TravelEndDate,
            Items = orderedCards.Select(card => BuildPreviewItem(card, dto, currencies, baseCurrency, outputCurrency)).ToList()
        };

        return preview;
    }

    public async Task<QuotePreviewDto?> BuildPreviewAsync(Guid id)
    {
        var builder = await GetEditAsync(id);
        return builder is null ? null : await BuildPreviewAsync(builder);
    }

    public async Task<Guid> CreateAsync(QuoteBuilderDto dto)
    {
        await HydrateClientSnapshotAsync(dto);
        var quote = new Quote
        {
            ReferenceNumber = await _numberingService.GenerateNextReferenceAsync(),
            Status = QuoteStatus.Draft,
            ClientId = dto.ClientId,
            ClientName = dto.ClientName.Trim(),
            ClientEmail = Normalize(dto.ClientEmail),
            ClientPhone = Normalize(dto.ClientPhone),
            OutputCurrencyCode = await ResolveCurrencyCodeAsync(dto.OutputCurrencyCode),
            MarkupPercentage = dto.MarkupPercentage,
            TemplateLayout = NormalizeTemplateLayout(dto.TemplateLayout),
            GroupBy = string.IsNullOrWhiteSpace(dto.GroupBy) ? "ratecard" : dto.GroupBy.Trim().ToLowerInvariant(),
            ShowImages = dto.ShowImages,
            ShowMealPlan = dto.ShowMealPlan,
            ShowFooter = dto.ShowFooter,
            ShowRoomDescriptions = dto.ShowRoomDescriptions,
            ValidUntil = dto.ValidUntil,
            TravelStartDate = dto.TravelStartDate,
            TravelEndDate = dto.TravelEndDate,
            FilterByTravelDates = dto.FilterByTravelDates,
            Notes = Normalize(dto.Notes),
            InternalNotes = Normalize(dto.InternalNotes)
        };

        ApplySelectedRateCards(quote, dto.SelectedRateCardIds);
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        await CreateVersionAsync(quote);
        return quote.Id;
    }

    public async Task UpdateAsync(Guid id, QuoteBuilderDto dto)
    {
        await HydrateClientSnapshotAsync(dto);
        var quote = await _db.Quotes
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Quote was not found.");

        var selectedRateCardIds = dto.SelectedRateCardIds.Distinct().ToList();

        quote.ClientId = dto.ClientId;
        quote.ClientName = dto.ClientName.Trim();
        quote.ClientEmail = Normalize(dto.ClientEmail);
        quote.ClientPhone = Normalize(dto.ClientPhone);
        quote.OutputCurrencyCode = await ResolveCurrencyCodeAsync(dto.OutputCurrencyCode);
        quote.MarkupPercentage = dto.MarkupPercentage;
        quote.TemplateLayout = NormalizeTemplateLayout(dto.TemplateLayout);
        quote.GroupBy = string.IsNullOrWhiteSpace(dto.GroupBy) ? "ratecard" : dto.GroupBy.Trim().ToLowerInvariant();
        quote.ShowImages = dto.ShowImages;
        quote.ShowMealPlan = dto.ShowMealPlan;
        quote.ShowFooter = dto.ShowFooter;
        quote.ShowRoomDescriptions = dto.ShowRoomDescriptions;
        quote.ValidUntil = dto.ValidUntil;
        quote.TravelStartDate = dto.TravelStartDate;
        quote.TravelEndDate = dto.TravelEndDate;
        quote.FilterByTravelDates = dto.FilterByTravelDates;
        quote.Notes = Normalize(dto.Notes);
        quote.InternalNotes = Normalize(dto.InternalNotes);

        var existingRateCards = await _db.QuoteRateCards
            .Where(x => x.QuoteId == id)
            .ToListAsync();

        if (existingRateCards.Count > 0)
        {
            _db.QuoteRateCards.RemoveRange(existingRateCards);
        }

        _db.QuoteRateCards.AddRange(selectedRateCardIds.Select((rateCardId, index) => new QuoteRateCard
        {
            QuoteId = id,
            RateCardId = rateCardId,
            SortOrder = index + 1
        }));

        await _db.SaveChangesAsync();
        await CreateVersionAsync(quote, selectedRateCardIds);
    }

    public async Task<QuoteDetailsDto?> GetDetailsAsync(Guid id)
    {
        return await _db.Quotes
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new QuoteDetailsDto
            {
                Id = x.Id,
                BookingId = _db.Bookings.Where(b => b.QuoteId == x.Id).Select(b => (Guid?)b.Id).FirstOrDefault(),
                BookingReferenceNumber = _db.Bookings.Where(b => b.QuoteId == x.Id).Select(b => b.BookingRef).FirstOrDefault(),
                ReferenceNumber = x.ReferenceNumber,
                Status = x.Status,
                ClientId = x.ClientId,
                ClientName = x.ClientName,
                ClientEmail = x.ClientEmail,
                ClientPhone = x.ClientPhone,
                OutputCurrencyCode = x.OutputCurrencyCode,
                MarkupPercentage = x.MarkupPercentage,
                TemplateLayout = x.TemplateLayout,
                ValidUntil = x.ValidUntil,
                TravelStartDate = x.TravelStartDate,
                TravelEndDate = x.TravelEndDate,
                FilterByTravelDates = x.FilterByTravelDates,
                Notes = x.Notes,
                InternalNotes = x.InternalNotes,
                RateCardCount = x.QuoteRateCards.Count,
                VersionCount = x.QuoteVersions.Count,
                UpdatedAt = x.UpdatedAt ?? x.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<QuoteVersionHistoryDto?> GetVersionHistoryAsync(Guid id)
    {
        var quote = await _db.Quotes
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.ReferenceNumber })
            .FirstOrDefaultAsync();

        if (quote is null)
        {
            return null;
        }

        var latestVersionNumber = await _db.QuoteVersions
            .AsNoTracking()
            .Where(x => x.QuoteId == id)
            .MaxAsync(x => (int?)x.VersionNumber) ?? 0;

        var versions = await _db.QuoteVersions
            .AsNoTracking()
            .Where(x => x.QuoteId == id)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync();

        return new QuoteVersionHistoryDto
        {
            QuoteId = quote.Id,
            ReferenceNumber = quote.ReferenceNumber,
            Versions = versions.Select(x =>
            {
                var snapshot = DeserializeSnapshot(x.SnapshotJson);
                return new QuoteVersionListItemDto
                {
                    Id = x.Id,
                    VersionNumber = x.VersionNumber,
                    IsCurrent = x.VersionNumber == latestVersionNumber,
                    CreatedBy = x.CreatedBy,
                    CreatedAt = x.CreatedAt,
                    RateCardCount = snapshot?.SelectedRateCardIds.Count ?? 0,
                    OutputCurrencyCode = snapshot?.OutputCurrencyCode ?? "USD"
                };
            }).ToList()
        };
    }

    public async Task<QuoteVersionDetailsDto?> GetVersionDetailsAsync(Guid id, Guid versionId)
    {
        var version = await _db.QuoteVersions
            .AsNoTracking()
            .Include(x => x.Quote)
            .FirstOrDefaultAsync(x => x.QuoteId == id && x.Id == versionId);

        if (version is null)
        {
            return null;
        }

        return new QuoteVersionDetailsDto
        {
            QuoteId = version.QuoteId,
            ReferenceNumber = version.Quote?.ReferenceNumber ?? string.Empty,
            VersionId = version.Id,
            VersionNumber = version.VersionNumber,
            CreatedBy = version.CreatedBy,
            CreatedAt = version.CreatedAt,
            Snapshot = DeserializeSnapshot(version.SnapshotJson) ?? new QuoteVersionSnapshotDto()
        };
    }

    public async Task UpdateStatusAsync(Guid id, QuoteStatus status)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Quote was not found.");

        quote.Status = status;
        await _db.SaveChangesAsync();
    }

    private async Task HydrateClientSnapshotAsync(QuoteBuilderDto dto)
    {
        if (!dto.ClientId.HasValue)
        {
            return;
        }

        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ClientId.Value);
        if (client is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dto.ClientName))
        {
            dto.ClientName = client.Name;
        }

        if (string.IsNullOrWhiteSpace(dto.ClientEmail))
        {
            dto.ClientEmail = client.Email;
        }

        if (string.IsNullOrWhiteSpace(dto.ClientPhone))
        {
            dto.ClientPhone = client.Phone;
        }
    }

    private async Task CreateVersionAsync(Quote quote, IReadOnlyList<Guid>? selectedRateCardIds = null)
    {
        var latestVersionNumber = await _db.QuoteVersions
            .Where(x => x.QuoteId == quote.Id)
            .MaxAsync(x => (int?)x.VersionNumber) ?? 0;

        var snapshot = BuildSnapshot(quote, selectedRateCardIds);
        _db.QuoteVersions.Add(new QuoteVersion
        {
            QuoteId = quote.Id,
            VersionNumber = latestVersionNumber + 1,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions)
        });

        await _db.SaveChangesAsync();
    }

    private static QuoteVersionSnapshotDto BuildSnapshot(Quote quote, IReadOnlyList<Guid>? selectedRateCardIds = null)
    {
        return new QuoteVersionSnapshotDto
        {
            ClientName = quote.ClientName,
            ClientEmail = quote.ClientEmail,
            ClientPhone = quote.ClientPhone,
            OutputCurrencyCode = quote.OutputCurrencyCode,
            MarkupPercentage = quote.MarkupPercentage,
            TemplateLayout = quote.TemplateLayout,
            GroupBy = quote.GroupBy,
            ShowImages = quote.ShowImages,
            ShowMealPlan = quote.ShowMealPlan,
            ShowFooter = quote.ShowFooter,
            ShowRoomDescriptions = quote.ShowRoomDescriptions,
            ValidUntil = quote.ValidUntil,
            TravelStartDate = quote.TravelStartDate,
            TravelEndDate = quote.TravelEndDate,
            FilterByTravelDates = quote.FilterByTravelDates,
            Notes = quote.Notes,
            InternalNotes = quote.InternalNotes,
            SelectedRateCardIds = selectedRateCardIds?.ToList() ?? quote.QuoteRateCards
                .OrderBy(x => x.SortOrder)
                .Select(x => x.RateCardId)
                .ToList()
        };
    }

    private static QuoteVersionSnapshotDto? DeserializeSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QuoteVersionSnapshotDto>(json, SnapshotJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private QuotePreviewItemDto BuildPreviewItem(RateCard card, QuoteBuilderDto dto, List<Currency> currencies, Currency? baseCurrency, Currency? outputCurrency)
    {
        var roomTypes = card.Seasons
            .SelectMany(x => x.Rates)
            .Where(x => x.RoomType != null)
            .Select(x => x.RoomType!)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new QuotePreviewRoomTypeDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description
            })
            .ToList();

        var seasons = card.Seasons
            .Where(x => !dto.FilterByTravelDates || SeasonOverlapsTravelWindow(x.StartDate, x.EndDate, dto.TravelStartDate, dto.TravelEndDate))
            .OrderBy(x => x.SortOrder)
            .Select(season => new QuotePreviewSeasonDto
            {
                Id = season.Id,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                IsBlackout = season.IsBlackout,
                Notes = season.Notes,
                Rates = roomTypes.Select(roomType =>
                {
                    var rate = season.Rates.FirstOrDefault(x => x.RoomTypeId == roomType.Id);
                    return new QuotePreviewRateDto
                    {
                        RoomTypeId = roomType.Id,
                        RoomTypeCode = roomType.Code,
                        WeekdayRate = ConvertAndMarkup(rate?.WeekdayRate ?? 0m, card.ContractCurrencyCode, outputCurrency?.Code ?? dto.OutputCurrencyCode, dto.MarkupPercentage, currencies, baseCurrency),
                        WeekendRate = rate?.WeekendRate.HasValue == true
                            ? ConvertAndMarkup(rate.WeekendRate.Value, card.ContractCurrencyCode, outputCurrency?.Code ?? dto.OutputCurrencyCode, dto.MarkupPercentage, currencies, baseCurrency)
                            : null,
                        IsIncluded = rate?.IsIncluded ?? true
                    };
                }).ToList()
            })
            .ToList();

        return new QuotePreviewItemDto
        {
            RateCardId = card.Id,
            RateCardName = card.Name,
            HotelName = card.InventoryItem?.Name ?? "Unknown hotel",
            Description = card.InventoryItem?.Description,
            DestinationName = card.InventoryItem?.Destination?.Name,
            ImageUrl = card.InventoryItem?.ImageUrl,
            Rating = card.InventoryItem?.Rating,
            MealPlanCode = card.DefaultMealPlan?.Code,
            MealPlanName = card.DefaultMealPlan?.Name,
            ContractCurrencyCode = card.ContractCurrencyCode,
            Status = card.Status,
            RoomTypes = roomTypes,
            Seasons = seasons
        };
    }

    private void ApplySelectedRateCards(Quote quote, List<Guid> selectedRateCardIds)
    {
        var orderedIds = selectedRateCardIds.Distinct().ToList();
        for (var i = 0; i < orderedIds.Count; i++)
        {
            quote.QuoteRateCards.Add(new QuoteRateCard
            {
                RateCardId = orderedIds[i],
                SortOrder = i + 1
            });
        }
    }

    private async Task<List<QuoteClientOptionDto>> GetClientOptionsAsync()
    {
        return await _db.Clients
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new QuoteClientOptionDto
            {
                Id = x.Id,
                Label = string.IsNullOrWhiteSpace(x.Email) ? x.Name : $"{x.Name} - {x.Email}"
            })
            .ToListAsync();
    }

    private async Task<List<string>> GetCurrencyOptionsAsync()
    {
        var options = await _db.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsBaseCurrency)
            .ThenBy(x => x.Code)
            .Select(x => x.Code)
            .ToListAsync();

        if (options.Count == 0)
        {
            options.Add("USD");
        }

        return options;
    }

    private async Task<List<QuoteRateCardOptionDto>> GetAvailableRateCardsAsync(List<Guid> selectedRateCardIds)
    {
        var selected = selectedRateCardIds.ToHashSet();
        var options = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
                .ThenInclude(x => x!.Destination)
            .Include(x => x.Seasons)
            .Where(x => x.Status != RateCardStatus.Archived)
            .OrderByDescending(x => x.Status == RateCardStatus.Active)
            .ThenBy(x => x.InventoryItem!.Name)
            .ThenBy(x => x.Name)
            .Select(x => new QuoteRateCardOptionDto
            {
                Id = x.Id,
                RateCardName = x.Name,
                HotelName = x.InventoryItem != null ? x.InventoryItem.Name : "Unknown hotel",
                DestinationName = x.InventoryItem != null && x.InventoryItem.Destination != null ? x.InventoryItem.Destination.Name : null,
                ContractCurrencyCode = x.ContractCurrencyCode,
                Status = x.Status,
                SeasonCount = x.Seasons.Count,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo,
                IsSelected = false
            })
            .ToListAsync();

        foreach (var option in options)
        {
            option.IsSelected = selected.Contains(option.Id);
        }

        return options;
    }

    private async Task<string> ResolveCurrencyCodeAsync(string requestedCurrency)
    {
        var normalized = requestedCurrency?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var match = await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(x => x.IsActive && x.Code == normalized);
            if (match is not null)
            {
                return match.Code;
            }
        }

        return await _db.Currencies.AsNoTracking().Where(x => x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync() ?? "USD";
    }

    private static bool TryParseStatus(string? status, out QuoteStatus parsedStatus)
        => Enum.TryParse(status, true, out parsedStatus);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeTemplateLayout(string? layout)
    {
        return layout?.Trim().ToLowerInvariant() switch
        {
            "list" => "list",
            "compact" => "compact",
            _ => "grid"
        };
    }

    private static bool SeasonOverlapsTravelWindow(DateOnly seasonStart, DateOnly seasonEnd, DateOnly? travelStart, DateOnly? travelEnd)
    {
        if (!travelStart.HasValue || !travelEnd.HasValue)
        {
            return true;
        }

        return seasonStart <= travelEnd.Value && seasonEnd >= travelStart.Value;
    }

    private static decimal ConvertAndMarkup(decimal amount, string sourceCurrencyCode, string targetCurrencyCode, decimal markupPercentage, List<Currency> currencies, Currency? baseCurrency)
    {
        var source = currencies.FirstOrDefault(x => string.Equals(x.Code, sourceCurrencyCode, StringComparison.OrdinalIgnoreCase)) ?? baseCurrency;
        var target = currencies.FirstOrDefault(x => string.Equals(x.Code, targetCurrencyCode, StringComparison.OrdinalIgnoreCase)) ?? baseCurrency;

        if (source is null || target is null)
        {
            return Math.Round(amount * (1m + (markupPercentage / 100m)), 2, MidpointRounding.AwayFromZero);
        }

        var baseAmount = source.ExchangeRate == 0m ? amount : amount / source.ExchangeRate;
        var converted = baseAmount * target.ExchangeRate;
        var markedUp = converted * (1m + (markupPercentage / 100m));
        return Math.Round(markedUp, 2, MidpointRounding.AwayFromZero);
    }
}
