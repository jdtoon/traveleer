using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;

namespace saas.Modules.RateCards.Services;

public interface IRateCardImportExportService
{
    Task<RateCardJsonExportDto> ExportJsonAsync(Guid rateCardId, string? exportedBy = null);
    Task<RateCardJsonExportBundleDto> ExportAllJsonAsync(string? exportedBy = null);
    Task<string> ExportCsvAsync(Guid rateCardId);
    Task<RateCardJsonImportPreviewDto> PreviewJsonImportAsync(string jsonContent);
    Task<RateCardJsonImportResultDto> ExecuteJsonImportAsync(string importToken, Dictionary<int, string>? actions = null);
    Task<RateCardCsvImportPreviewDto> PreviewCsvImportAsync(Guid rateCardId, string csvContent);
    Task<RateCardCsvImportResultDto> ExecuteCsvImportAsync(Guid rateCardId, string importToken);
}

public class RateCardImportExportService : IRateCardImportExportService
{
    private static readonly TimeSpan ImportSessionLifetime = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TenantDbContext _db;
    private readonly ICacheService _cache;

    public RateCardImportExportService(TenantDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<RateCardJsonExportDto> ExportJsonAsync(Guid rateCardId, string? exportedBy = null)
    {
        var rateCard = await GetRateCardForExportAsync(rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        return new RateCardJsonExportDto
        {
            ExportVersion = "1.0",
            ExportedAt = DateTime.UtcNow,
            ExportedBy = string.IsNullOrWhiteSpace(exportedBy) ? null : exportedBy.Trim(),
            RateCard = MapExportCard(rateCard)
        };
    }

    public async Task<string> ExportCsvAsync(Guid rateCardId)
    {
        var rateCard = await GetRateCardForExportAsync(rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        var builder = new StringBuilder();
        builder.AppendLine("SeasonName,RoomTypeCode,RoomTypeName,RateCategoryCode,RateCategoryName,WeekdayRate,WeekendRate,IsIncluded");

        foreach (var season in rateCard.Seasons.OrderBy(x => x.SortOrder))
        {
            foreach (var rate in season.Rates
                .OrderBy(x => x.RoomType != null ? x.RoomType.SortOrder : int.MaxValue)
                .ThenBy(x => x.RateCategory != null ? x.RateCategory.SortOrder : int.MaxValue)
                .ThenBy(x => x.RoomType != null ? x.RoomType.Name : x.RateCategory != null ? x.RateCategory.Name : string.Empty))
            {
                builder.AppendLine(string.Join(',',
                    EscapeCsv(season.Name),
                    EscapeCsv(rate.RoomType?.Code ?? string.Empty),
                    EscapeCsv(rate.RoomType?.Name ?? string.Empty),
                    EscapeCsv(rate.RateCategory?.Code ?? string.Empty),
                    EscapeCsv(rate.RateCategory?.Name ?? string.Empty),
                    rate.WeekdayRate.ToString("0.00", CultureInfo.InvariantCulture),
                    rate.WeekendRate.HasValue ? rate.WeekendRate.Value.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                    rate.IsIncluded ? "true" : "false"));
            }
        }

        return builder.ToString();
    }

    public async Task<RateCardJsonExportBundleDto> ExportAllJsonAsync(string? exportedBy = null)
    {
        var rateCards = await _db.RateCards
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
            .OrderBy(x => x.Name)
            .ToListAsync();

        return new RateCardJsonExportBundleDto
        {
            ExportVersion = "1.0",
            ExportedAt = DateTime.UtcNow,
            ExportedBy = string.IsNullOrWhiteSpace(exportedBy) ? null : exportedBy.Trim(),
            RateCards = rateCards.Select(MapExportCard).ToList()
        };
    }

    public async Task<RateCardJsonImportPreviewDto> PreviewJsonImportAsync(string jsonContent)
    {
        var preview = new RateCardJsonImportPreviewDto();
        var cards = DeserializeImportCards(jsonContent);

        if (cards.Count == 0)
        {
            preview.ErrorMessage = "The JSON file did not contain any rate cards.";
            return preview;
        }

        var existingRateCards = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
            .ToListAsync();
        var inventoryItems = await _db.InventoryItems.AsNoTracking().Include(x => x.Destination).ToListAsync();
        var destinations = await _db.Destinations.AsNoTracking().ToListAsync();

        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (string.IsNullOrWhiteSpace(card.Name))
            {
                preview.Warnings.Add($"Item {i + 1} is missing a rate-card name and will be skipped.");
                continue;
            }

            if (card.Seasons.Count == 0)
            {
                preview.Warnings.Add($"{card.Name} has no seasons and will be skipped.");
                continue;
            }

            var existing = existingRateCards.FirstOrDefault(x =>
                string.Equals(x.Name, card.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.InventoryItem?.Name, card.InventoryItemName, StringComparison.OrdinalIgnoreCase) &&
                x.InventoryItem != null && x.InventoryItem.Kind == card.InventoryKind);

            var createsDestination = !string.IsNullOrWhiteSpace(card.DestinationName) && !destinations.Any(x => string.Equals(x.Name, card.DestinationName, StringComparison.OrdinalIgnoreCase));
            var createsInventory = !inventoryItems.Any(x =>
                x.Kind == card.InventoryKind &&
                string.Equals(x.Name, card.InventoryItemName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Destination?.Name, card.DestinationName, StringComparison.OrdinalIgnoreCase));

            preview.Items.Add(new RateCardJsonImportPreviewItemDto
            {
                Index = i,
                Name = card.Name,
                InventoryItemName = card.InventoryItemName,
                DestinationName = card.DestinationName,
                SeasonCount = card.Seasons.Count,
                IsDuplicate = existing is not null,
                ExistingRateCardId = existing?.Id,
                CreatesDestination = createsDestination,
                CreatesInventoryItem = createsInventory
            });
        }

        if (preview.Items.Count == 0)
        {
            preview.ErrorMessage = "No valid rate cards were found in the import file.";
            return preview;
        }

        preview.ImportToken = Guid.NewGuid().ToString("N");
        await _cache.SetAsync(JsonCacheKey(preview.ImportToken), new RateCardJsonImportSession { RateCards = cards }, ImportSessionLifetime);
        return preview;
    }

    public async Task<RateCardJsonImportResultDto> ExecuteJsonImportAsync(string importToken, Dictionary<int, string>? actions = null)
    {
        var session = await _cache.GetAsync<RateCardJsonImportSession>(JsonCacheKey(importToken))
            ?? throw new InvalidOperationException("Import session expired. Upload the JSON again.");

        var result = new RateCardJsonImportResultDto();
        actions ??= new Dictionary<int, string>();

        foreach (var tuple in session.RateCards.Select((card, index) => new { card, index }))
        {
            var card = tuple.card;
            if (string.IsNullOrWhiteSpace(card.Name) || card.Seasons.Count == 0)
            {
                result.SkippedCount++;
                continue;
            }

            var inventory = await EnsureImportedInventoryAsync(card);
            var existing = await _db.RateCards
                .Include(x => x.InventoryItem)
                .Include(x => x.Seasons)
                    .ThenInclude(x => x.Rates)
                .FirstOrDefaultAsync(x => x.Name == card.Name && x.InventoryItem != null && x.InventoryItem.Name == card.InventoryItemName);

            var action = actions.TryGetValue(tuple.index, out var selectedAction) ? selectedAction : "skip";

            if (existing is not null && string.Equals(action, "skip", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedCount++;
                continue;
            }

            RateCard target;
            if (existing is not null && string.Equals(action, "replace", StringComparison.OrdinalIgnoreCase))
            {
                _db.RoomRates.RemoveRange(existing.Seasons.SelectMany(x => x.Rates));
                _db.RateSeasons.RemoveRange(existing.Seasons);
                target = existing;
            }
            else
            {
                target = new RateCard();
                _db.RateCards.Add(target);
            }

            target.Name = existing is not null && string.Equals(action, "copy", StringComparison.OrdinalIgnoreCase)
                ? await BuildImportedCopyNameAsync(card.Name)
                : card.Name.Trim();
            target.InventoryItemId = inventory.Id;
            target.DefaultMealPlanId = card.InventoryKind == InventoryItemKind.Hotel
                ? await EnsureImportedMealPlanAsync(card.DefaultMealPlanCode, card.DefaultMealPlanName)
                : null;
            target.ContractCurrencyCode = await ResolveImportedCurrencyAsync(card.ContractCurrencyCode);
            target.ValidFrom = card.ValidFrom;
            target.ValidTo = card.ValidTo;
            target.Notes = Normalize(card.Notes);
            target.Status = RateCardStatus.Draft;

            await _db.SaveChangesAsync();

            await ImportCardSeasonsAsync(target.Id, card.InventoryKind, card.Seasons);
            result.ImportedCount++;
            result.RateCardIds.Add(target.Id);
        }

        await _cache.RemoveAsync(JsonCacheKey(importToken));
        return result;
    }

    public async Task<RateCardCsvImportPreviewDto> PreviewCsvImportAsync(Guid rateCardId, string csvContent)
    {
        var rateCard = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
            .Include(x => x.Seasons)
            .FirstOrDefaultAsync(x => x.Id == rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        var inventoryKind = rateCard.InventoryItem?.Kind ?? InventoryItemKind.Hotel;
        var roomTypes = inventoryKind == InventoryItemKind.Hotel
            ? await _db.RoomTypes
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToListAsync()
            : [];
        var rateCategoryType = ToRateCategoryType(inventoryKind);
        var rateCategories = rateCategoryType.HasValue
            ? await _db.RateCategories
                .AsNoTracking()
                .Where(x => x.IsActive && x.ForType == rateCategoryType.Value)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToListAsync()
            : [];

        var preview = new RateCardCsvImportPreviewDto
        {
            RateCardId = rateCardId,
            RateCardName = rateCard.Name
        };

        var rows = ParseCsv(csvContent);
        if (rows.Count == 0)
        {
            preview.ErrorMessage = "The CSV file is empty.";
            return preview;
        }

        var header = rows[0];
        var index = BuildHeaderIndex(header);
        var hasHotelColumns = index.ContainsKey("roomtypecode") || index.ContainsKey("roomtypename");
        var hasCategoryColumns = index.ContainsKey("ratecategorycode") || index.ContainsKey("ratecategoryname");
        if (!index.ContainsKey("seasonname") || !index.ContainsKey("weekdayrate") || (inventoryKind == InventoryItemKind.Hotel ? !hasHotelColumns : !hasCategoryColumns))
        {
            preview.ErrorMessage = inventoryKind == InventoryItemKind.Hotel
                ? "CSV must include SeasonName, WeekdayRate, and RoomTypeCode or RoomTypeName columns."
                : "CSV must include SeasonName, WeekdayRate, and RateCategoryCode or RateCategoryName columns.";
            return preview;
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validRows = new List<RateCardCsvImportSessionRow>();

        for (var rowNumber = 1; rowNumber < rows.Count; rowNumber++)
        {
            var row = rows[rowNumber];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var previewRow = BuildPreviewRow(rowNumber + 1, row, index, rateCard.Seasons.ToList(), roomTypes, rateCategories, inventoryKind, seenKeys);
            preview.Rows.Add(previewRow);
            if (previewRow.IsValid)
            {
                validRows.Add(new RateCardCsvImportSessionRow
                {
                    SeasonId = previewRow.SeasonId!.Value,
                    RoomTypeId = previewRow.RoomTypeId,
                    RateCategoryId = previewRow.RateCategoryId,
                    SeasonName = previewRow.SeasonName,
                    RoomTypeCode = previewRow.RoomTypeCode,
                    RoomTypeName = previewRow.RoomTypeName,
                    RateCategoryCode = previewRow.RateCategoryCode,
                    RateCategoryName = previewRow.RateCategoryName,
                    WeekdayRate = previewRow.WeekdayRate!.Value,
                    WeekendRate = previewRow.WeekendRate,
                    IsIncluded = previewRow.IsIncluded
                });
            }
            else if (!string.IsNullOrWhiteSpace(previewRow.ErrorMessage))
            {
                preview.Warnings.Add($"Line {previewRow.LineNumber}: {previewRow.ErrorMessage}");
            }
        }

        preview.ValidRowCount = validRows.Count;
        preview.InvalidRowCount = preview.Rows.Count - preview.ValidRowCount;

        if (validRows.Count == 0)
        {
            preview.ErrorMessage = preview.Rows.Count == 0
                ? "The CSV file did not contain any import rows."
                : "No valid rows were found. Fix the highlighted issues and try again.";
            return preview;
        }

        preview.ImportToken = Guid.NewGuid().ToString("N");
        await _cache.SetAsync(CacheKey(preview.ImportToken), new RateCardCsvImportSession
        {
            RateCardId = rateCardId,
            RateCardName = rateCard.Name,
            Rows = validRows
        }, ImportSessionLifetime);

        return preview;
    }

    public async Task<RateCardCsvImportResultDto> ExecuteCsvImportAsync(Guid rateCardId, string importToken)
    {
        var session = await _cache.GetAsync<RateCardCsvImportSession>(CacheKey(importToken))
            ?? throw new InvalidOperationException("Import session expired. Upload the CSV again.");

        if (session.RateCardId != rateCardId)
        {
            throw new InvalidOperationException("Import session does not match this rate card.");
        }

        var rateCard = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
            .FirstOrDefaultAsync(x => x.Id == rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");
        var inventoryKind = rateCard.InventoryItem?.Kind ?? InventoryItemKind.Hotel;

        var roomRates = await _db.RoomRates
            .Include(x => x.RateSeason)
            .Where(x => x.RateSeason != null && x.RateSeason.RateCardId == rateCardId)
            .ToListAsync();

        foreach (var row in session.Rows)
        {
            var roomRate = inventoryKind == InventoryItemKind.Hotel
                ? roomRates.FirstOrDefault(x => x.RateSeasonId == row.SeasonId && x.RoomTypeId == row.RoomTypeId)
                : roomRates.FirstOrDefault(x => x.RateSeasonId == row.SeasonId && x.RateCategoryId == row.RateCategoryId);
            if (roomRate is null)
            {
                roomRate = new RoomRate
                {
                    RateSeasonId = row.SeasonId,
                    RoomTypeId = row.RoomTypeId,
                    RateCategoryId = row.RateCategoryId
                };
                _db.RoomRates.Add(roomRate);
                roomRates.Add(roomRate);
            }

            roomRate.WeekdayRate = row.WeekdayRate;
            roomRate.WeekendRate = row.WeekendRate;
            roomRate.IsIncluded = row.IsIncluded;
        }

        await _db.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKey(importToken));

        return new RateCardCsvImportResultDto
        {
            ImportedRowCount = session.Rows.Count,
            RateCardId = rateCardId,
            RateCardName = session.RateCardName
        };
    }

    private async Task<RateCard?> GetRateCardForExportAsync(Guid rateCardId)
    {
        return await _db.RateCards
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
            .FirstOrDefaultAsync(x => x.Id == rateCardId);
    }

    private static RateCardJsonExportCardDto MapExportCard(RateCard rateCard)
    {
        return new RateCardJsonExportCardDto
        {
            Name = rateCard.Name,
            Status = rateCard.Status,
            InventoryKind = rateCard.InventoryItem?.Kind ?? InventoryItemKind.Hotel,
            InventoryItemName = rateCard.InventoryItem?.Name ?? "Unknown product",
            DestinationName = rateCard.InventoryItem?.Destination?.Name,
            ContractCurrencyCode = rateCard.ContractCurrencyCode,
            DefaultMealPlanCode = rateCard.DefaultMealPlan?.Code,
            DefaultMealPlanName = rateCard.DefaultMealPlan?.Name,
            ValidFrom = rateCard.ValidFrom,
            ValidTo = rateCard.ValidTo,
            Notes = rateCard.Notes,
            Seasons = rateCard.Seasons
                .OrderBy(x => x.SortOrder)
                .Select(x => new RateCardJsonExportSeasonDto
                {
                    Name = x.Name,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    SortOrder = x.SortOrder,
                    IsBlackout = x.IsBlackout,
                    Notes = x.Notes,
                    Rates = x.Rates
                        .OrderBy(r => r.RoomType != null ? r.RoomType.SortOrder : int.MaxValue)
                        .ThenBy(r => r.RateCategory != null ? r.RateCategory.SortOrder : int.MaxValue)
                        .ThenBy(r => r.RoomType != null ? r.RoomType.Name : r.RateCategory != null ? r.RateCategory.Name : string.Empty)
                        .Select(r => new RateCardJsonExportRateDto
                        {
                            RoomTypeCode = r.RoomType?.Code,
                            RoomTypeName = r.RoomType?.Name,
                            RateCategoryCode = r.RateCategory?.Code,
                            RateCategoryName = r.RateCategory?.Name,
                            WeekdayRate = r.WeekdayRate,
                            WeekendRate = r.WeekendRate,
                            IsIncluded = r.IsIncluded
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private List<RateCardJsonExportCardDto> DeserializeImportCards(string jsonContent)
    {
        try
        {
            var single = JsonSerializer.Deserialize<RateCardJsonExportDto>(jsonContent, JsonOptions);
            if (single?.RateCard is not null && !string.IsNullOrWhiteSpace(single.RateCard.Name))
            {
                return [single.RateCard];
            }

            var bundle = JsonSerializer.Deserialize<RateCardJsonExportBundleDto>(jsonContent, JsonOptions);
            return bundle?.RateCards ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The JSON file is invalid.", ex);
        }
    }

    private async Task<saas.Modules.Inventory.Entities.InventoryItem> EnsureImportedInventoryAsync(RateCardJsonExportCardDto card)
    {
        var destination = await EnsureImportedDestinationAsync(card.DestinationName);
        var destinationId = destination?.Id;

        var existing = await _db.InventoryItems
            .FirstOrDefaultAsync(x => x.Kind == card.InventoryKind && x.Name == card.InventoryItemName && x.DestinationId == destinationId);

        if (existing is not null)
        {
            return existing;
        }

        var inventory = new saas.Modules.Inventory.Entities.InventoryItem
        {
            Name = card.InventoryItemName.Trim(),
            Kind = card.InventoryKind,
            DestinationId = destination?.Id,
            CreatedAt = DateTime.UtcNow
        };

        _db.InventoryItems.Add(inventory);
        await _db.SaveChangesAsync();
        return inventory;
    }

    private async Task<saas.Modules.Settings.Entities.Destination?> EnsureImportedDestinationAsync(string? destinationName)
    {
        if (string.IsNullOrWhiteSpace(destinationName))
        {
            return null;
        }

        var existing = await _db.Destinations.FirstOrDefaultAsync(x => x.Name == destinationName.Trim());
        if (existing is not null)
        {
            return existing;
        }

        var destination = new saas.Modules.Settings.Entities.Destination
        {
            Name = destinationName.Trim(),
            IsActive = true,
            SortOrder = (await _db.Destinations.Select(x => (int?)x.SortOrder).MaxAsync() ?? 0) + 10,
            CreatedAt = DateTime.UtcNow
        };
        _db.Destinations.Add(destination);
        await _db.SaveChangesAsync();
        return destination;
    }

    private async Task<Guid?> EnsureImportedMealPlanAsync(string? mealPlanCode, string? mealPlanName)
    {
        if (string.IsNullOrWhiteSpace(mealPlanCode) && string.IsNullOrWhiteSpace(mealPlanName))
        {
            return null;
        }

        var normalizedCode = NormalizeCode(string.IsNullOrWhiteSpace(mealPlanCode) ? BuildCodeFromName(mealPlanName!) : mealPlanCode!);
        var existing = await _db.MealPlans.FirstOrDefaultAsync(x => x.Code == normalizedCode);
        if (existing is not null)
        {
            return existing.Id;
        }

        var mealPlan = new saas.Modules.Settings.Entities.MealPlan
        {
            Code = normalizedCode,
            Name = string.IsNullOrWhiteSpace(mealPlanName) ? normalizedCode : mealPlanName.Trim(),
            SortOrder = (await _db.MealPlans.Select(x => (int?)x.SortOrder).MaxAsync() ?? 0) + 10,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.MealPlans.Add(mealPlan);
        await _db.SaveChangesAsync();
        return mealPlan.Id;
    }

    private async Task<string> ResolveImportedCurrencyAsync(string currencyCode)
    {
        var normalized = NormalizeCode(currencyCode);
        var existing = await _db.Currencies.FirstOrDefaultAsync(x => x.Code == normalized && x.IsActive);
        return existing?.Code ?? await _db.Currencies.Where(x => x.IsBaseCurrency).Select(x => x.Code).FirstOrDefaultAsync() ?? "USD";
    }

    private async Task ImportCardSeasonsAsync(Guid rateCardId, InventoryItemKind inventoryKind, List<RateCardJsonExportSeasonDto> seasons)
    {
        var roomTypes = await _db.RoomTypes.ToListAsync();
        var rateCategoryType = ToRateCategoryType(inventoryKind);
        var rateCategories = rateCategoryType.HasValue
            ? await _db.RateCategories.Where(x => x.ForType == rateCategoryType.Value).ToListAsync()
            : [];

        foreach (var seasonDto in seasons.OrderBy(x => x.SortOrder))
        {
            var season = new RateSeason
            {
                RateCardId = rateCardId,
                Name = seasonDto.Name.Trim(),
                StartDate = seasonDto.StartDate,
                EndDate = seasonDto.EndDate,
                SortOrder = seasonDto.SortOrder,
                IsBlackout = seasonDto.IsBlackout,
                Notes = Normalize(seasonDto.Notes)
            };

            if (inventoryKind == InventoryItemKind.Hotel)
            {
                foreach (var roomType in roomTypes.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
                {
                    season.Rates.Add(new RoomRate
                    {
                        RoomTypeId = roomType.Id,
                        WeekdayRate = 0m,
                        WeekendRate = null,
                        IsIncluded = true
                    });
                }
            }
            else
            {
                foreach (var category in rateCategories.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
                {
                    season.Rates.Add(new RoomRate
                    {
                        RateCategoryId = category.Id,
                        WeekdayRate = 0m,
                        WeekendRate = null,
                        IsIncluded = true
                    });
                }
            }

            foreach (var rateDto in seasonDto.Rates)
            {
                if (inventoryKind == InventoryItemKind.Hotel)
                {
                    var roomType = roomTypes.FirstOrDefault(x =>
                        (!string.IsNullOrWhiteSpace(rateDto.RoomTypeCode) && x.Code == NormalizeCode(rateDto.RoomTypeCode)) ||
                        (!string.IsNullOrWhiteSpace(rateDto.RoomTypeName) && x.Name == rateDto.RoomTypeName.Trim()));

                    if (roomType is null)
                    {
                        roomType = new saas.Modules.Settings.Entities.RoomType
                        {
                            Code = NormalizeCode(string.IsNullOrWhiteSpace(rateDto.RoomTypeCode) ? BuildCodeFromName(rateDto.RoomTypeName) : rateDto.RoomTypeCode),
                            Name = string.IsNullOrWhiteSpace(rateDto.RoomTypeName) ? (rateDto.RoomTypeCode ?? "Imported Room") : rateDto.RoomTypeName.Trim(),
                            SortOrder = (roomTypes.Select(x => (int?)x.SortOrder).Max() ?? 0) + 10,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };

                        _db.RoomTypes.Add(roomType);
                        await _db.SaveChangesAsync();
                        roomTypes.Add(roomType);

                        season.Rates.Add(new RoomRate
                        {
                            RoomTypeId = roomType.Id,
                            WeekdayRate = 0m,
                            WeekendRate = null,
                            IsIncluded = true
                        });
                    }

                    var roomRate = season.Rates.First(x => x.RoomTypeId == roomType.Id);
                    roomRate.WeekdayRate = rateDto.WeekdayRate;
                    roomRate.WeekendRate = rateDto.WeekendRate;
                    roomRate.IsIncluded = rateDto.IsIncluded;
                }
                else
                {
                    var rateCategory = rateCategories.FirstOrDefault(x =>
                        (!string.IsNullOrWhiteSpace(rateDto.RateCategoryCode) && x.Code == NormalizeCode(rateDto.RateCategoryCode)) ||
                        (!string.IsNullOrWhiteSpace(rateDto.RateCategoryName) && x.Name == rateDto.RateCategoryName!.Trim()));

                    if (rateCategory is null)
                    {
                        rateCategory = new saas.Modules.Settings.Entities.RateCategory
                        {
                            ForType = rateCategoryType ?? InventoryType.Excursion,
                            Code = NormalizeCode(string.IsNullOrWhiteSpace(rateDto.RateCategoryCode) ? BuildCodeFromName(rateDto.RateCategoryName) : rateDto.RateCategoryCode),
                            Name = string.IsNullOrWhiteSpace(rateDto.RateCategoryName) ? (rateDto.RateCategoryCode ?? "Imported Category") : rateDto.RateCategoryName.Trim(),
                            SortOrder = (rateCategories.Select(x => (int?)x.SortOrder).Max() ?? 0) + 10,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };

                        _db.RateCategories.Add(rateCategory);
                        await _db.SaveChangesAsync();
                        rateCategories.Add(rateCategory);

                        season.Rates.Add(new RoomRate
                        {
                            RateCategoryId = rateCategory.Id,
                            WeekdayRate = 0m,
                            WeekendRate = null,
                            IsIncluded = true
                        });
                    }

                    var roomRate = season.Rates.First(x => x.RateCategoryId == rateCategory.Id);
                    roomRate.WeekdayRate = rateDto.WeekdayRate;
                    roomRate.WeekendRate = rateDto.WeekendRate;
                    roomRate.IsIncluded = rateDto.IsIncluded;
                }
            }

            _db.RateSeasons.Add(season);
            await _db.SaveChangesAsync();
        }
    }

    private async Task<string> BuildImportedCopyNameAsync(string baseName)
    {
        var candidate = $"{baseName.Trim()} (Imported)";
        var suffix = 2;
        while (await _db.RateCards.AnyAsync(x => x.Name == candidate))
        {
            candidate = $"{baseName.Trim()} (Imported {suffix})";
            suffix++;
        }

        return candidate;
    }

    private static RateCardCsvImportPreviewRowDto BuildPreviewRow(
        int lineNumber,
        List<string> row,
        IReadOnlyDictionary<string, int> index,
        List<RateSeason> seasons,
        List<saas.Modules.Settings.Entities.RoomType> roomTypes,
        List<saas.Modules.Settings.Entities.RateCategory> rateCategories,
        InventoryItemKind inventoryKind,
        HashSet<string> seenKeys)
    {
        var seasonName = ValueAt(row, index, "seasonname");
        var roomTypeCode = ValueAt(row, index, "roomtypecode");
        var roomTypeName = ValueAt(row, index, "roomtypename");
        var rateCategoryCode = ValueAt(row, index, "ratecategorycode");
        var rateCategoryName = ValueAt(row, index, "ratecategoryname");
        var weekdayRateText = ValueAt(row, index, "weekdayrate");
        var weekendRateText = ValueAt(row, index, "weekendrate");
        var isIncludedText = ValueAt(row, index, "isincluded");

        var preview = new RateCardCsvImportPreviewRowDto
        {
            LineNumber = lineNumber,
            SeasonName = seasonName,
            RoomTypeCode = roomTypeCode,
            RoomTypeName = roomTypeName,
            RateCategoryCode = rateCategoryCode,
            RateCategoryName = rateCategoryName,
            RawWeekdayRate = weekdayRateText,
            RawWeekendRate = weekendRateText,
            RawIsIncluded = isIncludedText,
            IsIncluded = ParseIncluded(isIncludedText)
        };

        if (string.IsNullOrWhiteSpace(seasonName))
        {
            preview.ErrorMessage = "SeasonName is required.";
            return preview;
        }

        var season = seasons.FirstOrDefault(x => string.Equals(x.Name, seasonName, StringComparison.OrdinalIgnoreCase));
        if (season is null)
        {
            preview.ErrorMessage = $"Season '{seasonName}' was not found on this rate card.";
            return preview;
        }

        saas.Modules.Settings.Entities.RoomType? roomType = null;
        saas.Modules.Settings.Entities.RateCategory? rateCategory = null;

        if (inventoryKind == InventoryItemKind.Hotel)
        {
            roomType = roomTypes.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(roomTypeCode) && string.Equals(x.Code, roomTypeCode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(roomTypeName) && string.Equals(x.Name, roomTypeName, StringComparison.OrdinalIgnoreCase)));
            if (roomType is null)
            {
                preview.ErrorMessage = $"Room type '{roomTypeCode ?? roomTypeName}' was not found.";
                return preview;
            }
        }
        else
        {
            rateCategory = rateCategories.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(rateCategoryCode) && string.Equals(x.Code, rateCategoryCode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(rateCategoryName) && string.Equals(x.Name, rateCategoryName, StringComparison.OrdinalIgnoreCase)));
            if (rateCategory is null)
            {
                preview.ErrorMessage = $"Rate category '{rateCategoryCode ?? rateCategoryName}' was not found.";
                return preview;
            }
        }

        if (!decimal.TryParse(weekdayRateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var weekdayRate) || weekdayRate < 0)
        {
            preview.ErrorMessage = "WeekdayRate must be a valid non-negative decimal.";
            return preview;
        }

        decimal? weekendRate = null;
        if (!string.IsNullOrWhiteSpace(weekendRateText))
        {
            if (!decimal.TryParse(weekendRateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedWeekendRate) || parsedWeekendRate < 0)
            {
                preview.ErrorMessage = "WeekendRate must be blank or a valid non-negative decimal.";
                return preview;
            }

            weekendRate = parsedWeekendRate;
        }

        var duplicateKey = inventoryKind == InventoryItemKind.Hotel
            ? $"{season.Id}:{roomType!.Id}"
            : $"{season.Id}:{rateCategory!.Id}";
        if (!seenKeys.Add(duplicateKey))
        {
            preview.ErrorMessage = inventoryKind == InventoryItemKind.Hotel
                ? "This CSV contains duplicate updates for the same season and room type."
                : "This CSV contains duplicate updates for the same season and rate category.";
            return preview;
        }

        preview.SeasonId = season.Id;
        preview.RoomTypeId = roomType?.Id;
        preview.RateCategoryId = rateCategory?.Id;
        preview.RoomTypeCode = roomType?.Code ?? string.Empty;
        preview.RoomTypeName = roomType?.Name ?? string.Empty;
        preview.RateCategoryCode = rateCategory?.Code ?? string.Empty;
        preview.RateCategoryName = rateCategory?.Name ?? string.Empty;
        preview.WeekdayRate = weekdayRate;
        preview.WeekendRate = weekendRate;
        preview.IsValid = true;
        return preview;
    }

    private static List<List<string>> ParseCsv(string csvContent)
    {
        var rows = new List<List<string>>();
        using var reader = new StringReader(csvContent.Replace("\r\n", "\n"));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rows.Add(ParseCsvLine(line));
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        values.Add(current.ToString().Trim());
        return values;
    }

    private static Dictionary<string, int> BuildHeaderIndex(List<string> header)
        => header
            .Select((value, index) => new { Key = NormalizeHeader(value), Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);

    private static string ValueAt(List<string> row, IReadOnlyDictionary<string, int> index, string key)
        => index.TryGetValue(key, out var position) && position < row.Count ? row[position].Trim() : string.Empty;

    private static string NormalizeHeader(string value)
        => value.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static bool ParseIncluded(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "1" or "included" => true,
            "false" or "no" or "n" or "0" or "excluded" => false,
            _ => true
        };
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static string CacheKey(string token) => $"ratecards:csv-import:{token}";

    private static string JsonCacheKey(string token) => $"ratecards:json-import:{token}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCode(string value)
        => value.Trim().ToUpperInvariant();

    private static string BuildCodeFromName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "IMPORTED";
        }

        var cleaned = new string(value.Trim().ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "IMPORTED" : cleaned[..Math.Min(cleaned.Length, 20)];
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
}

public class RateCardCsvImportSession
{
    public Guid RateCardId { get; set; }
    public string RateCardName { get; set; } = string.Empty;
    public List<RateCardCsvImportSessionRow> Rows { get; set; } = [];
}

public class RateCardCsvImportSessionRow
{
    public Guid SeasonId { get; set; }
    public Guid? RoomTypeId { get; set; }
    public Guid? RateCategoryId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string RoomTypeCode { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public string RateCategoryCode { get; set; } = string.Empty;
    public string RateCategoryName { get; set; } = string.Empty;
    public decimal WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; }
}

public class RateCardJsonImportSession
{
    public List<RateCardJsonExportCardDto> RateCards { get; set; } = [];
}
