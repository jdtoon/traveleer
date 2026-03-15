using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Tenant;
using saas.Modules.Settings.DTOs;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Services;

public interface ISettingsService
{
    Task<List<RoomTypeListItemDto>> GetRoomTypesAsync();
    Task<RoomTypeDto> CreateEmptyRoomTypeAsync();
    Task<RoomTypeDto?> GetRoomTypeAsync(Guid id);
    Task CreateRoomTypeAsync(RoomTypeDto dto);
    Task UpdateRoomTypeAsync(Guid id, RoomTypeDto dto);
    Task DeleteRoomTypeAsync(Guid id);

    Task<List<MealPlanListItemDto>> GetMealPlansAsync();
    Task<MealPlanDto> CreateEmptyMealPlanAsync();
    Task<MealPlanDto?> GetMealPlanAsync(Guid id);
    Task CreateMealPlanAsync(MealPlanDto dto);
    Task UpdateMealPlanAsync(Guid id, MealPlanDto dto);
    Task DeleteMealPlanAsync(Guid id);

    Task<List<CurrencyListItemDto>> GetCurrenciesAsync();
    Task<CurrencyDto> CreateEmptyCurrencyAsync();
    Task<CurrencyDto?> GetCurrencyAsync(Guid id);
    Task CreateCurrencyAsync(CurrencyDto dto);
    Task UpdateCurrencyAsync(Guid id, CurrencyDto dto);
    Task DeleteCurrencyAsync(Guid id);
    Task SetBaseCurrencyAsync(Guid id);

    Task<PaginatedList<DestinationListItemDto>> GetDestinationsAsync(int page = 1, int pageSize = 12);
    Task<DestinationDto> CreateEmptyDestinationAsync();
    Task<DestinationDto?> GetDestinationAsync(Guid id);
    Task CreateDestinationAsync(DestinationDto dto);
    Task UpdateDestinationAsync(Guid id, DestinationDto dto);
    Task DeleteDestinationAsync(Guid id);

    Task<PaginatedList<SupplierListItemDto>> GetSuppliersAsync(int page = 1, int pageSize = 12);
    Task<SupplierDto> CreateEmptySupplierAsync();
    Task<SupplierDto?> GetSupplierAsync(Guid id);
    Task CreateSupplierAsync(SupplierDto dto);
    Task UpdateSupplierAsync(Guid id, SupplierDto dto);
    Task DeleteSupplierAsync(Guid id);

    Task<List<RateCategoryGroupDto>> GetRateCategoryGroupsAsync();
    Task<RateCategoryDto> CreateEmptyRateCategoryAsync();
    Task<RateCategoryDto?> GetRateCategoryAsync(Guid id);
    Task CreateRateCategoryAsync(RateCategoryDto dto);
    Task UpdateRateCategoryAsync(Guid id, RateCategoryDto dto);
    Task DeleteRateCategoryAsync(Guid id);

    Task<bool> RoomTypeCodeExistsAsync(string code, Guid? excludeId = null);
    Task<bool> MealPlanCodeExistsAsync(string code, Guid? excludeId = null);
    Task<bool> CurrencyCodeExistsAsync(string code, Guid? excludeId = null);
    Task<bool> RateCategoryCodeExistsAsync(InventoryType type, string code, Guid? excludeId = null);
}

public class SettingsService : ISettingsService
{
    private readonly TenantDbContext _db;

    public SettingsService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<RoomTypeListItemDto>> GetRoomTypesAsync()
        => await _db.RoomTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new RoomTypeListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .ToListAsync();

    public Task<RoomTypeDto> CreateEmptyRoomTypeAsync() => Task.FromResult(new RoomTypeDto { IsActive = true });

    public async Task<RoomTypeDto?> GetRoomTypeAsync(Guid id)
        => await _db.RoomTypes.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new RoomTypeDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();

    public async Task CreateRoomTypeAsync(RoomTypeDto dto)
    {
        _db.RoomTypes.Add(new RoomType
        {
            Code = NormalizeCode(dto.Code),
            Name = dto.Name.Trim(),
            Description = Normalize(dto.Description),
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateRoomTypeAsync(Guid id, RoomTypeDto dto)
    {
        var entity = await _db.RoomTypes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Room type {id} was not found.");
        entity.Code = NormalizeCode(dto.Code);
        entity.Name = dto.Name.Trim();
        entity.Description = Normalize(dto.Description);
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteRoomTypeAsync(Guid id)
    {
        var entity = await _db.RoomTypes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Room type {id} was not found.");
        _db.RoomTypes.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<List<MealPlanListItemDto>> GetMealPlansAsync()
        => await _db.MealPlans.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new MealPlanListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .ToListAsync();

    public Task<MealPlanDto> CreateEmptyMealPlanAsync() => Task.FromResult(new MealPlanDto { IsActive = true });

    public async Task<MealPlanDto?> GetMealPlanAsync(Guid id)
        => await _db.MealPlans.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new MealPlanDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();

    public async Task CreateMealPlanAsync(MealPlanDto dto)
    {
        _db.MealPlans.Add(new MealPlan
        {
            Code = NormalizeCode(dto.Code),
            Name = dto.Name.Trim(),
            Description = Normalize(dto.Description),
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateMealPlanAsync(Guid id, MealPlanDto dto)
    {
        var entity = await _db.MealPlans.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Meal plan {id} was not found.");
        entity.Code = NormalizeCode(dto.Code);
        entity.Name = dto.Name.Trim();
        entity.Description = Normalize(dto.Description);
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteMealPlanAsync(Guid id)
    {
        var entity = await _db.MealPlans.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Meal plan {id} was not found.");
        _db.MealPlans.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CurrencyListItemDto>> GetCurrenciesAsync()
        => await _db.Currencies.AsNoTracking()
            .OrderByDescending(x => x.IsBaseCurrency).ThenBy(x => x.Code)
            .Select(x => new CurrencyListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Symbol = x.Symbol,
                ExchangeRate = x.ExchangeRate,
                DefaultMarkup = x.DefaultMarkup,
                RoundingRule = x.RoundingRule,
                IsBaseCurrency = x.IsBaseCurrency,
                IsActive = x.IsActive,
                LastManualUpdate = x.LastManualUpdate
            })
            .ToListAsync();

    public Task<CurrencyDto> CreateEmptyCurrencyAsync() => Task.FromResult(new CurrencyDto { IsActive = true, ExchangeRate = 1m });

    public async Task<CurrencyDto?> GetCurrencyAsync(Guid id)
        => await _db.Currencies.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CurrencyDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Symbol = x.Symbol,
                ExchangeRate = x.ExchangeRate,
                DefaultMarkup = x.DefaultMarkup,
                RoundingRule = x.RoundingRule,
                IsBaseCurrency = x.IsBaseCurrency,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();

    public async Task CreateCurrencyAsync(CurrencyDto dto)
    {
        if (dto.IsBaseCurrency)
            await ClearBaseCurrencyFlagAsync();

        _db.Currencies.Add(new Currency
        {
            Code = NormalizeCode(dto.Code),
            Name = dto.Name.Trim(),
            Symbol = Normalize(dto.Symbol),
            ExchangeRate = dto.ExchangeRate,
            DefaultMarkup = dto.DefaultMarkup,
            RoundingRule = dto.RoundingRule,
            IsBaseCurrency = dto.IsBaseCurrency,
            IsActive = dto.IsActive,
            LastManualUpdate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateCurrencyAsync(Guid id, CurrencyDto dto)
    {
        if (dto.IsBaseCurrency)
            await ClearBaseCurrencyFlagAsync(id);

        var entity = await _db.Currencies.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Currency {id} was not found.");
        entity.Code = NormalizeCode(dto.Code);
        entity.Name = dto.Name.Trim();
        entity.Symbol = Normalize(dto.Symbol);
        entity.ExchangeRate = dto.ExchangeRate;
        entity.DefaultMarkup = dto.DefaultMarkup;
        entity.RoundingRule = dto.RoundingRule;
        entity.IsBaseCurrency = dto.IsBaseCurrency;
        entity.IsActive = dto.IsActive;
        entity.LastManualUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteCurrencyAsync(Guid id)
    {
        var entity = await _db.Currencies.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Currency {id} was not found.");
        if (entity.IsBaseCurrency)
            throw new InvalidOperationException("Base currency cannot be deleted.");
        _db.Currencies.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task SetBaseCurrencyAsync(Guid id)
    {
        await ClearBaseCurrencyFlagAsync(id);
        var entity = await _db.Currencies.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Currency {id} was not found.");
        entity.IsBaseCurrency = true;
        entity.LastManualUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<PaginatedList<DestinationListItemDto>> GetDestinationsAsync(int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Destinations.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new DestinationListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                CountryCode = x.CountryCode,
                CountryName = x.CountryName,
                Region = x.Region,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            });

        return await PaginatedList<DestinationListItemDto>.CreateAsync(query, normalizedPage, normalizedPageSize);
    }

    public Task<DestinationDto> CreateEmptyDestinationAsync() => Task.FromResult(new DestinationDto { IsActive = true });

    public async Task<DestinationDto?> GetDestinationAsync(Guid id)
        => await _db.Destinations.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new DestinationDto
            {
                Id = x.Id,
                Name = x.Name,
                CountryCode = x.CountryCode,
                CountryName = x.CountryName,
                Region = x.Region,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();

    public async Task CreateDestinationAsync(DestinationDto dto)
    {
        _db.Destinations.Add(new Destination
        {
            Name = dto.Name.Trim(),
            CountryCode = NormalizeUpper(dto.CountryCode, 2),
            CountryName = Normalize(dto.CountryName),
            Region = Normalize(dto.Region),
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateDestinationAsync(Guid id, DestinationDto dto)
    {
        var entity = await _db.Destinations.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Destination {id} was not found.");
        entity.Name = dto.Name.Trim();
        entity.CountryCode = NormalizeUpper(dto.CountryCode, 2);
        entity.CountryName = Normalize(dto.CountryName);
        entity.Region = Normalize(dto.Region);
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteDestinationAsync(Guid id)
    {
        var entity = await _db.Destinations.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Destination {id} was not found.");
        _db.Destinations.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<PaginatedList<SupplierListItemDto>> GetSuppliersAsync(int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 6, 48);

        var query = _db.Suppliers.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SupplierListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                ContactName = x.ContactName,
                ContactEmail = x.ContactEmail,
                ContactPhone = x.ContactPhone,
                IsActive = x.IsActive
            });

        return await PaginatedList<SupplierListItemDto>.CreateAsync(query, normalizedPage, normalizedPageSize);
    }

    public Task<SupplierDto> CreateEmptySupplierAsync() => Task.FromResult(new SupplierDto { IsActive = true });

    public async Task<SupplierDto?> GetSupplierAsync(Guid id)
        => await _db.Suppliers.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new SupplierDto
            {
                Id = x.Id,
                Name = x.Name,
                ContactName = x.ContactName,
                ContactEmail = x.ContactEmail,
                ContactPhone = x.ContactPhone,
                Notes = x.Notes,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();

    public async Task CreateSupplierAsync(SupplierDto dto)
    {
        _db.Suppliers.Add(new Supplier
        {
            Name = dto.Name.Trim(),
            ContactName = Normalize(dto.ContactName),
            ContactEmail = Normalize(dto.ContactEmail),
            ContactPhone = Normalize(dto.ContactPhone),
            Notes = Normalize(dto.Notes),
            IsActive = dto.IsActive
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateSupplierAsync(Guid id, SupplierDto dto)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Supplier {id} was not found.");
        entity.Name = dto.Name.Trim();
        entity.ContactName = Normalize(dto.ContactName);
        entity.ContactEmail = Normalize(dto.ContactEmail);
        entity.ContactPhone = Normalize(dto.ContactPhone);
        entity.Notes = Normalize(dto.Notes);
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteSupplierAsync(Guid id)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Supplier {id} was not found.");
        _db.Suppliers.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<List<RateCategoryGroupDto>> GetRateCategoryGroupsAsync()
    {
        var items = await _db.RateCategories.AsNoTracking()
            .OrderBy(x => x.ForType).ThenBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new RateCategoryListItemDto
            {
                Id = x.Id,
                ForType = x.ForType,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                Capacity = x.Capacity,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .ToListAsync();

        return Enum.GetValues<InventoryType>()
            .Select(type => new RateCategoryGroupDto
            {
                Type = type,
                Label = GetInventoryTypeLabel(type),
                Items = items.Where(x => x.ForType == type).ToList()
            })
            .ToList();
    }

    public Task<RateCategoryDto> CreateEmptyRateCategoryAsync() => Task.FromResult(new RateCategoryDto { IsActive = true, ForType = InventoryType.Flight });

    public async Task<RateCategoryDto?> GetRateCategoryAsync(Guid id)
        => await _db.RateCategories.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new RateCategoryDto
            {
                Id = x.Id,
                ForType = x.ForType,
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                Capacity = x.Capacity,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync();

    public async Task CreateRateCategoryAsync(RateCategoryDto dto)
    {
        _db.RateCategories.Add(new RateCategory
        {
            ForType = dto.ForType,
            Code = NormalizeCode(dto.Code),
            Name = dto.Name.Trim(),
            Description = Normalize(dto.Description),
            Capacity = dto.ForType == InventoryType.Transfer ? dto.Capacity : null,
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive
        });
        await _db.SaveChangesAsync();
    }

    public async Task UpdateRateCategoryAsync(Guid id, RateCategoryDto dto)
    {
        var entity = await _db.RateCategories.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Rate category {id} was not found.");
        entity.ForType = dto.ForType;
        entity.Code = NormalizeCode(dto.Code);
        entity.Name = dto.Name.Trim();
        entity.Description = Normalize(dto.Description);
        entity.Capacity = dto.ForType == InventoryType.Transfer ? dto.Capacity : null;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteRateCategoryAsync(Guid id)
    {
        var entity = await _db.RateCategories.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException($"Rate category {id} was not found.");
        _db.RateCategories.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public Task<bool> RoomTypeCodeExistsAsync(string code, Guid? excludeId = null)
        => _db.RoomTypes.AnyAsync(x => x.Code == NormalizeCode(code) && (!excludeId.HasValue || x.Id != excludeId.Value));

    public Task<bool> MealPlanCodeExistsAsync(string code, Guid? excludeId = null)
        => _db.MealPlans.AnyAsync(x => x.Code == NormalizeCode(code) && (!excludeId.HasValue || x.Id != excludeId.Value));

    public Task<bool> CurrencyCodeExistsAsync(string code, Guid? excludeId = null)
        => _db.Currencies.AnyAsync(x => x.Code == NormalizeCode(code) && (!excludeId.HasValue || x.Id != excludeId.Value));

    public Task<bool> RateCategoryCodeExistsAsync(InventoryType type, string code, Guid? excludeId = null)
        => _db.RateCategories.AnyAsync(x => x.ForType == type && x.Code == NormalizeCode(code) && (!excludeId.HasValue || x.Id != excludeId.Value));

    private async Task ClearBaseCurrencyFlagAsync(Guid? excludeId = null)
    {
        var baseCurrencies = await _db.Currencies.Where(x => x.IsBaseCurrency && (!excludeId.HasValue || x.Id != excludeId.Value)).ToListAsync();
        foreach (var currency in baseCurrencies)
            currency.IsBaseCurrency = false;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCode(string code)
        => code.Trim().ToUpperInvariant();

    private static string? NormalizeUpper(string? value, int maxLength)
    {
        var normalized = Normalize(value);
        if (normalized is null)
            return null;
        normalized = normalized.ToUpperInvariant();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string GetInventoryTypeLabel(InventoryType type)
        => type switch
        {
            InventoryType.Flight => "Flights",
            InventoryType.Excursion => "Excursions",
            InventoryType.Transfer => "Transfers",
            InventoryType.Visa => "Visas",
            _ => type.ToString()
        };
}
