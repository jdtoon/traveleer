using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Services;

public interface IRateCardService
{
    Task<PaginatedList<RateCardListItemDto>> GetListAsync(string? status = null, string? search = null, int page = 1, int pageSize = 12);
    Task<RateCardFormDto> CreateEmptyAsync();
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
                InventoryItemName = x.InventoryItem != null ? x.InventoryItem.Name : "Unknown hotel",
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

    public async Task<RateCardFormDto> CreateEmptyAsync()
    {
        return new RateCardFormDto
        {
            InventoryOptions = await GetHotelInventoryOptionsAsync(),
            TemplateOptions = await _templateService.GetOptionsAsync(InventoryItemKind.Hotel),
            MealPlanOptions = await GetMealPlanOptionsAsync(),
            CurrencyOptions = await GetCurrencyOptionsAsync()
        };
    }

    public async Task<Guid> CreateAsync(RateCardFormDto dto)
    {
        var hotel = await _db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == dto.InventoryItemId && x.Kind == InventoryItemKind.Hotel)
            ?? throw new InvalidOperationException("Hotel inventory item was not found.");

        var card = new RateCard
        {
            Name = dto.Name.Trim(),
            InventoryItemId = hotel.Id,
            DefaultMealPlanId = dto.DefaultMealPlanId,
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
        var roomTypes = await _db.RoomTypes
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

        var card = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
                .ThenInclude(x => x!.Destination)
            .Include(x => x.DefaultMealPlan)
            .Include(x => x.Seasons.OrderBy(s => s.SortOrder))
                .ThenInclude(x => x.Rates)
                    .ThenInclude(x => x.RoomType)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (card is null)
        {
            return null;
        }

        return new RateCardDetailsDto
        {
            Id = card.Id,
            Name = card.Name,
            Status = card.Status,
            InventoryItemId = card.InventoryItemId,
            InventoryItemName = card.InventoryItem != null ? card.InventoryItem.Name : "Unknown hotel",
            DestinationName = card.InventoryItem != null && card.InventoryItem.Destination != null ? card.InventoryItem.Destination.Name : null,
            ContractCurrencyCode = card.ContractCurrencyCode,
            DefaultMealPlanName = card.DefaultMealPlan != null ? card.DefaultMealPlan.Name : null,
            ValidFrom = card.ValidFrom,
            ValidTo = card.ValidTo,
            Notes = card.Notes,
            UpdatedAt = card.UpdatedAt ?? card.CreatedAt,
            AvailableTemplateCount = await _db.RateCardTemplates.AsNoTracking().CountAsync(x => x.ForKind == InventoryItemKind.Hotel),
            RoomTypes = roomTypes,
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
                    Rates = roomTypes.Select(roomType =>
                    {
                        var rate = s.Rates.FirstOrDefault(r => r.RoomTypeId == roomType.Id);
                        return new RateCardRateCellDto
                        {
                            RoomRateId = rate != null ? rate.Id : null,
                            RoomTypeId = roomType.Id,
                            RoomTypeCode = roomType.Code,
                            RoomTypeName = roomType.Name,
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

        var roomTypes = await _db.RoomTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        if (roomTypes.Count > 0)
        {
            _db.RoomRates.AddRange(roomTypes.Select(roomType => new RoomRate
            {
                RateSeasonId = season.Id,
                RoomTypeId = roomType.Id,
                WeekdayRate = 0m,
                WeekendRate = null,
                IsIncluded = true
            }));
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
        var seasonExists = await _db.RateSeasons
            .AsNoTracking()
            .AnyAsync(x => x.Id == dto.RateSeasonId && x.RateCardId == dto.RateCardId);
        if (!seasonExists)
        {
            throw new InvalidOperationException("Season was not found.");
        }

        var roomTypeExists = await _db.RoomTypes.AsNoTracking().AnyAsync(x => x.Id == dto.RoomTypeId && x.IsActive);
        if (!roomTypeExists)
        {
            throw new InvalidOperationException("Room type was not found.");
        }

        var roomRate = await _db.RoomRates.FirstOrDefaultAsync(x => x.RateSeasonId == dto.RateSeasonId && x.RoomTypeId == dto.RoomTypeId);
        if (roomRate is null)
        {
            roomRate = new RoomRate
            {
                RateSeasonId = dto.RateSeasonId,
                RoomTypeId = dto.RoomTypeId
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

    private async Task<List<RateCardOptionDto>> GetHotelInventoryOptionsAsync()
    {
        return await _db.InventoryItems
            .AsNoTracking()
            .Include(x => x.Destination)
            .Where(x => x.Kind == InventoryItemKind.Hotel)
            .OrderBy(x => x.Name)
            .Select(x => new RateCardOptionDto
            {
                Id = x.Id,
                Label = x.Destination != null ? $"{x.Name} - {x.Destination.Name}" : x.Name
            })
            .ToListAsync();
    }

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
