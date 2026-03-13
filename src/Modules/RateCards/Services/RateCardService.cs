using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;

namespace saas.Modules.RateCards.Services;

public interface IRateCardService
{
    Task<PaginatedList<RateCardListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12);
    Task<RateCardFormDto> CreateEmptyAsync(Guid? inventoryItemId = null);
    Task<Guid> CreateAsync(RateCardFormDto dto);
    Task<RateCardDetailsDto?> GetDetailsAsync(Guid id);
    Task<RateSeasonFormDto> CreateEmptySeasonAsync(Guid rateCardId);
    Task<RateSeasonFormDto?> GetSeasonAsync(Guid rateCardId, Guid seasonId);
    Task CreateSeasonAsync(Guid rateCardId, RateSeasonFormDto dto);
    Task UpdateSeasonAsync(Guid rateCardId, RateSeasonFormDto dto);
    Task DeleteSeasonAsync(Guid rateCardId, Guid seasonId);
    Task UpdateRateAsync(RateCardRateUpdateDto dto);
    Task ActivateAsync(Guid id);
    Task ArchiveAsync(Guid id);
    Task SetDraftAsync(Guid id);
    Task<Guid> DuplicateAsync(Guid id);
}

public class RateCardService : IRateCardService
{
    private readonly TenantDbContext _db;
    private readonly IRateCardTemplateService _templateService;

    public RateCardService(TenantDbContext db, IRateCardTemplateService templateService)
    {
        _db = db;
        _templateService = templateService;
    }

    public async Task<PaginatedList<RateCardListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
                .ThenInclude(x => x!.Destination)
            .Include(x => x.Seasons)
            .AsQueryable();

        if (TryParseStatus(status, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                x.ContractCurrencyCode.ToLower().Contains(term) ||
                (x.InventoryItem != null && x.InventoryItem.Name.ToLower().Contains(term)) ||
                (x.InventoryItem != null && x.InventoryItem.Destination != null && x.InventoryItem.Destination.Name.ToLower().Contains(term)));
        }

        var projected = query
            .OrderByDescending(x => x.Status == RateCardStatus.Active)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Select(x => new RateCardListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                InventoryItemName = x.InventoryItem != null ? x.InventoryItem.Name : "Unknown product",
                InventoryKind = x.InventoryItem != null ? x.InventoryItem.Kind : InventoryItemKind.Hotel,
                DestinationName = x.InventoryItem != null && x.InventoryItem.Destination != null ? x.InventoryItem.Destination.Name : null,
                ContractCurrencyCode = x.ContractCurrencyCode,
                Status = x.Status,
                SeasonCount = x.Seasons.Count,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo,
                UpdatedAt = x.UpdatedAt ?? x.CreatedAt
            });

        return await PaginatedList<RateCardListItemDto>.CreateAsync(projected, page, pageSize);
    }

    public async Task<RateCardFormDto> CreateEmptyAsync(Guid? inventoryItemId = null)
    {
        var selectedKind = await ResolveInventoryKindAsync(inventoryItemId);

        return new RateCardFormDto
        {
            InventoryItemId = inventoryItemId,
            InventoryOptions = await GetInventoryOptionsAsync(),
            TemplateOptions = await _templateService.GetOptionsAsync(selectedKind),
            MealPlanOptions = await GetMealPlanOptionsAsync(),
            CurrencyOptions = await GetCurrencyOptionsAsync()
        };
    }

    public async Task<Guid> CreateAsync(RateCardFormDto dto)
    {
        var inventoryItem = await _db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.InventoryItemId)
            ?? throw new InvalidOperationException("Inventory item was not found.");

        if (dto.TemplateId.HasValue)
        {
            var template = await _db.RateCardTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == dto.TemplateId.Value)
                ?? throw new InvalidOperationException("Rate card template was not found.");

            if (template.ForKind != inventoryItem.Kind)
            {
                throw new InvalidOperationException("Choose a template that matches the selected inventory type.");
            }
        }

        var card = new RateCard
        {
            Name = dto.Name.Trim(),
            InventoryItemId = inventoryItem.Id,
            DefaultMealPlanId = inventoryItem.Kind == InventoryItemKind.Hotel ? dto.DefaultMealPlanId : null,
            ContractCurrencyCode = await ResolveCurrencyAsync(dto.ContractCurrencyCode),
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            Notes = Normalize(dto.Notes),
            Status = RateCardStatus.Draft
        };

        _db.RateCards.Add(card);
        await _db.SaveChangesAsync();

        if (dto.TemplateId.HasValue)
        {
            var templateSeasons = await _templateService.GetSeasonDefinitionsAsync(dto.TemplateId.Value);
            var targetYear = dto.ValidFrom?.Year ?? DateTime.UtcNow.Year;
            var seasonForms = RateCardTemplateService.BuildSeasonForms(templateSeasons, targetYear);
            foreach (var seasonForm in seasonForms)
            {
                await CreateSeasonAsync(card.Id, seasonForm);
            }
        }

        return card.Id;
    }

    public async Task<RateCardDetailsDto?> GetDetailsAsync(Guid id)
    {
        var card = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
                .ThenInclude(x => x!.Destination)
            .Include(x => x.DefaultMealPlan)
            .Include(x => x.Seasons.OrderBy(s => s.SortOrder))
                .ThenInclude(x => x.Rates)
                    .ThenInclude(x => x.RoomType)
            .Include(x => x.Seasons)
                .ThenInclude(x => x.Rates)
                    .ThenInclude(x => x.RateCategory)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (card is null)
        {
            return null;
        }

        var inventoryKind = card.InventoryItem?.Kind ?? InventoryItemKind.Hotel;
        var dimensions = await GetPricingDimensionsAsync(inventoryKind);

        return new RateCardDetailsDto
        {
            Id = card.Id,
            Name = card.Name,
            Status = card.Status,
            InventoryItemId = card.InventoryItemId,
            InventoryItemName = card.InventoryItem != null ? card.InventoryItem.Name : "Unknown product",
            InventoryKind = inventoryKind,
            DestinationName = card.InventoryItem != null && card.InventoryItem.Destination != null ? card.InventoryItem.Destination.Name : null,
            ContractCurrencyCode = card.ContractCurrencyCode,
            DefaultMealPlanName = card.DefaultMealPlan != null ? card.DefaultMealPlan.Name : null,
            ValidFrom = card.ValidFrom,
            ValidTo = card.ValidTo,
            Notes = card.Notes,
            UpdatedAt = card.UpdatedAt ?? card.CreatedAt,
            AvailableTemplateCount = await _db.RateCardTemplates.AsNoTracking().CountAsync(x => x.ForKind == inventoryKind),
            RoomTypes = dimensions,
            Seasons = card.Seasons
                .OrderBy(s => s.SortOrder)
                .Select(s => new RateCardSeasonEditorDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    SortOrder = s.SortOrder,
                    IsBlackout = s.IsBlackout,
                    Notes = s.Notes,
                    Rates = dimensions.Select(dimension =>
                    {
                        var rate = inventoryKind == InventoryItemKind.Hotel
                            ? s.Rates.FirstOrDefault(r => r.RoomTypeId == dimension.Id)
                            : s.Rates.FirstOrDefault(r => r.RateCategoryId == dimension.Id);

                        return new RateCardRateCellDto
                        {
                            RoomRateId = rate != null ? rate.Id : null,
                            RoomTypeId = inventoryKind == InventoryItemKind.Hotel ? dimension.Id : null,
                            RateCategoryId = inventoryKind == InventoryItemKind.Hotel ? null : dimension.Id,
                            Code = dimension.Code,
                            Name = dimension.Name,
                            WeekdayRate = rate != null ? rate.WeekdayRate : 0m,
                            WeekendRate = rate != null ? rate.WeekendRate : null,
                            IsIncluded = rate == null || rate.IsIncluded
                        };
                    }).ToList()
                }).ToList()
        };
    }

    public async Task<RateSeasonFormDto> CreateEmptySeasonAsync(Guid rateCardId)
    {
        await EnsureRateCardExists(rateCardId);
        return new RateSeasonFormDto
        {
            RateCardId = rateCardId,
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
        };
    }

    public async Task<RateSeasonFormDto?> GetSeasonAsync(Guid rateCardId, Guid seasonId)
    {
        return await _db.RateSeasons
            .AsNoTracking()
            .Where(x => x.RateCardId == rateCardId && x.Id == seasonId)
            .Select(x => new RateSeasonFormDto
            {
                Id = x.Id,
                RateCardId = x.RateCardId,
                Name = x.Name,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                IsBlackout = x.IsBlackout,
                Notes = x.Notes
            })
            .FirstOrDefaultAsync();
    }

    public async Task CreateSeasonAsync(Guid rateCardId, RateSeasonFormDto dto)
    {
        var card = await _db.RateCards
            .Include(x => x.Seasons)
            .Include(x => x.InventoryItem)
            .FirstOrDefaultAsync(x => x.Id == rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        ValidateSeasonDates(dto.StartDate, dto.EndDate);
        await EnsureNoOverlapAsync(rateCardId, null, dto.StartDate, dto.EndDate);

        var sortOrder = card.Seasons.Count == 0 ? 10 : card.Seasons.Max(x => x.SortOrder) + 10;
        var season = new RateSeason
        {
            RateCardId = rateCardId,
            Name = dto.Name.Trim(),
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsBlackout = dto.IsBlackout,
            Notes = Normalize(dto.Notes),
            SortOrder = sortOrder
        };

        _db.RateSeasons.Add(season);
        await _db.SaveChangesAsync();

        var defaultRates = await BuildDefaultRatesAsync(season.Id, card.InventoryItem?.Kind ?? InventoryItemKind.Hotel);
        if (defaultRates.Count > 0)
        {
            _db.RoomRates.AddRange(defaultRates);
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateSeasonAsync(Guid rateCardId, RateSeasonFormDto dto)
    {
        var season = await _db.RateSeasons
            .FirstOrDefaultAsync(x => x.RateCardId == rateCardId && x.Id == dto.Id)
            ?? throw new InvalidOperationException("Season was not found.");

        ValidateSeasonDates(dto.StartDate, dto.EndDate);
        await EnsureNoOverlapAsync(rateCardId, season.Id, dto.StartDate, dto.EndDate);

        season.Name = dto.Name.Trim();
        season.StartDate = dto.StartDate;
        season.EndDate = dto.EndDate;
        season.IsBlackout = dto.IsBlackout;
        season.Notes = Normalize(dto.Notes);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteSeasonAsync(Guid rateCardId, Guid seasonId)
    {
        var season = await _db.RateSeasons
            .FirstOrDefaultAsync(x => x.RateCardId == rateCardId && x.Id == seasonId)
            ?? throw new InvalidOperationException("Season was not found.");

        _db.RateSeasons.Remove(season);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateRateAsync(RateCardRateUpdateDto dto)
    {
        var season = await _db.RateSeasons
            .AsNoTracking()
            .Include(x => x.RateCard)
                .ThenInclude(x => x!.InventoryItem)
            .FirstOrDefaultAsync(x => x.Id == dto.RateSeasonId && x.RateCardId == dto.RateCardId);
        if (season is null)
        {
            throw new InvalidOperationException("Season was not found.");
        }

        var inventoryKind = season.RateCard?.InventoryItem?.Kind ?? InventoryItemKind.Hotel;
        if (inventoryKind == InventoryItemKind.Hotel)
        {
            if (!dto.RoomTypeId.HasValue)
            {
                throw new InvalidOperationException("Room type was not found.");
            }

            var roomTypeExists = await _db.RoomTypes.AsNoTracking().AnyAsync(x => x.Id == dto.RoomTypeId.Value && x.IsActive);
            if (!roomTypeExists)
            {
                throw new InvalidOperationException("Room type was not found.");
            }
        }
        else
        {
            var categoryType = ToRateCategoryType(inventoryKind)
                ?? throw new InvalidOperationException("This inventory type does not support categorized rates.");

            if (!dto.RateCategoryId.HasValue)
            {
                throw new InvalidOperationException("Rate category was not found.");
            }

            var categoryExists = await _db.RateCategories
                .AsNoTracking()
                .AnyAsync(x => x.Id == dto.RateCategoryId.Value && x.IsActive && x.ForType == categoryType);
            if (!categoryExists)
            {
                throw new InvalidOperationException("Rate category was not found.");
            }
        }

        var roomRate = inventoryKind == InventoryItemKind.Hotel
            ? await _db.RoomRates.FirstOrDefaultAsync(x => x.RateSeasonId == dto.RateSeasonId && x.RoomTypeId == dto.RoomTypeId)
            : await _db.RoomRates.FirstOrDefaultAsync(x => x.RateSeasonId == dto.RateSeasonId && x.RateCategoryId == dto.RateCategoryId);

        if (roomRate is null)
        {
            roomRate = new RoomRate
            {
                RateSeasonId = dto.RateSeasonId,
                RoomTypeId = dto.RoomTypeId,
                RateCategoryId = dto.RateCategoryId
            };
            _db.RoomRates.Add(roomRate);
        }

        roomRate.WeekdayRate = dto.WeekdayRate;
        roomRate.WeekendRate = dto.WeekendRate;
        roomRate.IsIncluded = dto.IsIncluded;
        await _db.SaveChangesAsync();
    }

    public async Task ActivateAsync(Guid id)
    {
        var card = await _db.RateCards
            .Include(x => x.Seasons)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Rate card was not found.");

        if (card.Seasons.Count == 0)
        {
            throw new InvalidOperationException("Add at least one season before activating the rate card.");
        }

        var overlappingActiveCards = await _db.RateCards
            .Where(x => x.InventoryItemId == card.InventoryItemId && x.Id != card.Id && x.Status == RateCardStatus.Active)
            .ToListAsync();

        foreach (var otherCard in overlappingActiveCards)
        {
            otherCard.Status = RateCardStatus.Archived;
        }

        card.Status = RateCardStatus.Active;
        await _db.SaveChangesAsync();
    }

    public async Task ArchiveAsync(Guid id)
    {
        var card = await _db.RateCards.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Rate card was not found.");

        card.Status = RateCardStatus.Archived;
        await _db.SaveChangesAsync();
    }

    public async Task SetDraftAsync(Guid id)
    {
        var card = await _db.RateCards.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Rate card was not found.");

        card.Status = RateCardStatus.Draft;
        await _db.SaveChangesAsync();
    }

    public async Task<Guid> DuplicateAsync(Guid id)
    {
        var source = await _db.RateCards
            .Include(x => x.Seasons)
                .ThenInclude(x => x.Rates)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Rate card was not found.");

        var duplicate = new RateCard
        {
            Name = $"{source.Name} (Copy)",
            Status = RateCardStatus.Draft,
            InventoryItemId = source.InventoryItemId,
            DefaultMealPlanId = source.DefaultMealPlanId,
            ContractCurrencyCode = source.ContractCurrencyCode,
            ValidFrom = source.ValidFrom,
            ValidTo = source.ValidTo,
            Notes = source.Notes
        };

        foreach (var season in source.Seasons.OrderBy(x => x.SortOrder))
        {
            var clonedSeason = new RateSeason
            {
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                SortOrder = season.SortOrder,
                IsBlackout = season.IsBlackout,
                Notes = season.Notes
            };

            foreach (var rate in season.Rates)
            {
                clonedSeason.Rates.Add(new RoomRate
                {
                    RoomTypeId = rate.RoomTypeId,
                    RateCategoryId = rate.RateCategoryId,
                    WeekdayRate = rate.WeekdayRate,
                    WeekendRate = rate.WeekendRate,
                    IsIncluded = rate.IsIncluded
                });
            }

            duplicate.Seasons.Add(clonedSeason);
        }

        _db.RateCards.Add(duplicate);
        await _db.SaveChangesAsync();
        return duplicate.Id;
    }

    private async Task EnsureRateCardExists(Guid rateCardId)
    {
        var exists = await _db.RateCards.AsNoTracking().AnyAsync(x => x.Id == rateCardId);
        if (!exists)
        {
            throw new InvalidOperationException("Rate card was not found.");
        }
    }

    private async Task EnsureNoOverlapAsync(Guid rateCardId, Guid? currentSeasonId, DateOnly startDate, DateOnly endDate)
    {
        var overlaps = await _db.RateSeasons
            .AsNoTracking()
            .Where(x => x.RateCardId == rateCardId && (!currentSeasonId.HasValue || x.Id != currentSeasonId.Value))
            .AnyAsync(x => startDate <= x.EndDate && endDate >= x.StartDate);

        if (overlaps)
        {
            throw new InvalidOperationException("Season dates overlap with an existing season.");
        }
    }

    private async Task<List<RateCardOptionDto>> GetInventoryOptionsAsync()
    {
        return await _db.InventoryItems
            .AsNoTracking()
            .Include(x => x.Destination)
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Name)
            .Select(x => new RateCardOptionDto
            {
                Id = x.Id,
                Label = x.Destination != null ? $"{KindLabel(x.Kind)} - {x.Name} - {x.Destination.Name}" : $"{KindLabel(x.Kind)} - {x.Name}"
            })
            .ToListAsync();
    }

    private async Task<InventoryItemKind?> ResolveInventoryKindAsync(Guid? inventoryItemId)
    {
        if (!inventoryItemId.HasValue)
        {
            return null;
        }

        return await _db.InventoryItems
            .AsNoTracking()
            .Where(x => x.Id == inventoryItemId.Value)
            .Select(x => (InventoryItemKind?)x.Kind)
            .FirstOrDefaultAsync();
    }

    private async Task<List<RateCardRoomTypeDto>> GetPricingDimensionsAsync(InventoryItemKind kind)
    {
        if (kind == InventoryItemKind.Hotel)
        {
            return await _db.RoomTypes
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new RateCardRoomTypeDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name
                })
                .ToListAsync();
        }

        var rateCategoryType = ToRateCategoryType(kind);
        if (!rateCategoryType.HasValue)
        {
            return [];
        }

        return await _db.RateCategories
            .AsNoTracking()
            .Where(x => x.IsActive && x.ForType == rateCategoryType.Value)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new RateCardRoomTypeDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name
            })
            .ToListAsync();
    }

    private async Task<List<RoomRate>> BuildDefaultRatesAsync(Guid rateSeasonId, InventoryItemKind kind)
    {
        if (kind == InventoryItemKind.Hotel)
        {
            var roomTypes = await _db.RoomTypes
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ToListAsync();

            return roomTypes.Select(roomType => new RoomRate
            {
                RateSeasonId = rateSeasonId,
                RoomTypeId = roomType.Id,
                WeekdayRate = 0m,
                WeekendRate = null,
                IsIncluded = true
            }).ToList();
        }

        var rateCategoryType = ToRateCategoryType(kind);
        if (!rateCategoryType.HasValue)
        {
            return [];
        }

        var categories = await _db.RateCategories
            .AsNoTracking()
            .Where(x => x.IsActive && x.ForType == rateCategoryType.Value)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        return categories.Select(category => new RoomRate
        {
            RateSeasonId = rateSeasonId,
            RateCategoryId = category.Id,
            WeekdayRate = 0m,
            WeekendRate = null,
            IsIncluded = true
        }).ToList();
    }

    private static InventoryType? ToRateCategoryType(InventoryItemKind kind)
        => kind switch
        {
            InventoryItemKind.Flight => InventoryType.Flight,
            InventoryItemKind.Excursion => InventoryType.Excursion,
            InventoryItemKind.Transfer => InventoryType.Transfer,
            InventoryItemKind.Visa => InventoryType.Visa,
            _ => null
        };

    private static string KindLabel(InventoryItemKind kind)
        => kind switch
        {
            InventoryItemKind.Hotel => "Hotel",
            InventoryItemKind.Flight => "Flight",
            InventoryItemKind.Excursion => "Excursion",
            InventoryItemKind.Transfer => "Transfer",
            InventoryItemKind.Visa => "Visa",
            _ => "Other"
        };

    private async Task<List<RateCardOptionDto>> GetMealPlanOptionsAsync()
    {
        return await _db.MealPlans
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new RateCardOptionDto
            {
                Id = x.Id,
                Label = $"{x.Code} - {x.Name}"
            })
            .ToListAsync();
    }

    private async Task<List<string>> GetCurrencyOptionsAsync()
    {
        var currencies = await _db.Currencies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsBaseCurrency)
            .ThenBy(x => x.Code)
            .Select(x => x.Code)
            .ToListAsync();

        if (currencies.Count == 0)
        {
            currencies.Add("USD");
        }

        return currencies;
    }

    private async Task<string> ResolveCurrencyAsync(string? requestedCurrency)
    {
        if (!string.IsNullOrWhiteSpace(requestedCurrency))
        {
            var match = await _db.Currencies
                .AsNoTracking()
                .Where(x => x.IsActive && x.Code == requestedCurrency.Trim().ToUpper())
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return await _db.Currencies
            .AsNoTracking()
            .Where(x => x.IsBaseCurrency)
            .Select(x => x.Code)
            .FirstOrDefaultAsync() ?? "USD";
    }

    private static bool TryParseStatus(string? status, out RateCardStatus parsedStatus)
        => Enum.TryParse(status, true, out parsedStatus);

    private static void ValidateSeasonDates(DateOnly startDate, DateOnly endDate)
    {
        if (endDate <= startDate)
        {
            throw new InvalidOperationException("End date must be after start date.");
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
