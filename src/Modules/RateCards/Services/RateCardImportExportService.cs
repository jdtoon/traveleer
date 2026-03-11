using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Services;

public interface IRateCardImportExportService
{
    Task<RateCardJsonExportDto> ExportJsonAsync(Guid rateCardId, string? exportedBy = null);
    Task<string> ExportCsvAsync(Guid rateCardId);
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
            RateCard = new RateCardJsonExportCardDto
            {
                Name = rateCard.Name,
                Status = rateCard.Status,
                InventoryItemName = rateCard.InventoryItem?.Name ?? "Unknown hotel",
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
                            .ThenBy(r => r.RoomType != null ? r.RoomType.Name : string.Empty)
                            .Select(r => new RateCardJsonExportRateDto
                            {
                                RoomTypeCode = r.RoomType?.Code ?? string.Empty,
                                RoomTypeName = r.RoomType?.Name ?? string.Empty,
                                WeekdayRate = r.WeekdayRate,
                                WeekendRate = r.WeekendRate,
                                IsIncluded = r.IsIncluded
                            })
                            .ToList()
                    })
                    .ToList()
            }
        };
    }

    public async Task<string> ExportCsvAsync(Guid rateCardId)
    {
        var rateCard = await GetRateCardForExportAsync(rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        var builder = new StringBuilder();
        builder.AppendLine("SeasonName,RoomTypeCode,RoomTypeName,WeekdayRate,WeekendRate,IsIncluded");

        foreach (var season in rateCard.Seasons.OrderBy(x => x.SortOrder))
        {
            foreach (var rate in season.Rates.OrderBy(x => x.RoomType != null ? x.RoomType.SortOrder : int.MaxValue).ThenBy(x => x.RoomType != null ? x.RoomType.Name : string.Empty))
            {
                builder.AppendLine(string.Join(',',
                    EscapeCsv(season.Name),
                    EscapeCsv(rate.RoomType?.Code ?? string.Empty),
                    EscapeCsv(rate.RoomType?.Name ?? string.Empty),
                    rate.WeekdayRate.ToString("0.00", CultureInfo.InvariantCulture),
                    rate.WeekendRate.HasValue ? rate.WeekendRate.Value.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                    rate.IsIncluded ? "true" : "false"));
            }
        }

        return builder.ToString();
    }

    public async Task<RateCardCsvImportPreviewDto> PreviewCsvImportAsync(Guid rateCardId, string csvContent)
    {
        var rateCard = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.Seasons)
            .FirstOrDefaultAsync(x => x.Id == rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        var roomTypes = await _db.RoomTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

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
        if (!index.ContainsKey("seasonname") || !index.ContainsKey("weekdayrate") || (!index.ContainsKey("roomtypecode") && !index.ContainsKey("roomtypename")))
        {
            preview.ErrorMessage = "CSV must include SeasonName, WeekdayRate, and RoomTypeCode or RoomTypeName columns.";
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

            var previewRow = BuildPreviewRow(rowNumber + 1, row, index, rateCard.Seasons.ToList(), roomTypes, seenKeys);
            preview.Rows.Add(previewRow);
            if (previewRow.IsValid)
            {
                validRows.Add(new RateCardCsvImportSessionRow
                {
                    SeasonId = previewRow.SeasonId!.Value,
                    RoomTypeId = previewRow.RoomTypeId!.Value,
                    SeasonName = previewRow.SeasonName,
                    RoomTypeCode = previewRow.RoomTypeCode,
                    RoomTypeName = previewRow.RoomTypeName,
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

        var roomRates = await _db.RoomRates
            .Include(x => x.RateSeason)
            .Where(x => x.RateSeason != null && x.RateSeason.RateCardId == rateCardId)
            .ToListAsync();

        foreach (var row in session.Rows)
        {
            var roomRate = roomRates.FirstOrDefault(x => x.RateSeasonId == row.SeasonId && x.RoomTypeId == row.RoomTypeId);
            if (roomRate is null)
            {
                roomRate = new RoomRate
                {
                    RateSeasonId = row.SeasonId,
                    RoomTypeId = row.RoomTypeId
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
            .FirstOrDefaultAsync(x => x.Id == rateCardId);
    }

    private static RateCardCsvImportPreviewRowDto BuildPreviewRow(
        int lineNumber,
        List<string> row,
        IReadOnlyDictionary<string, int> index,
        List<RateSeason> seasons,
        List<saas.Modules.Settings.Entities.RoomType> roomTypes,
        HashSet<string> seenKeys)
    {
        var seasonName = ValueAt(row, index, "seasonname");
        var roomTypeCode = ValueAt(row, index, "roomtypecode");
        var roomTypeName = ValueAt(row, index, "roomtypename");
        var weekdayRateText = ValueAt(row, index, "weekdayrate");
        var weekendRateText = ValueAt(row, index, "weekendrate");
        var isIncludedText = ValueAt(row, index, "isincluded");

        var preview = new RateCardCsvImportPreviewRowDto
        {
            LineNumber = lineNumber,
            SeasonName = seasonName,
            RoomTypeCode = roomTypeCode,
            RoomTypeName = roomTypeName,
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

        var roomType = roomTypes.FirstOrDefault(x =>
            (!string.IsNullOrWhiteSpace(roomTypeCode) && string.Equals(x.Code, roomTypeCode, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(roomTypeName) && string.Equals(x.Name, roomTypeName, StringComparison.OrdinalIgnoreCase)));
        if (roomType is null)
        {
            preview.ErrorMessage = $"Room type '{roomTypeCode ?? roomTypeName}' was not found.";
            return preview;
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

        var duplicateKey = $"{season.Id}:{roomType.Id}";
        if (!seenKeys.Add(duplicateKey))
        {
            preview.ErrorMessage = "This CSV contains duplicate updates for the same season and room type.";
            return preview;
        }

        preview.SeasonId = season.Id;
        preview.RoomTypeId = roomType.Id;
        preview.RoomTypeCode = roomType.Code;
        preview.RoomTypeName = roomType.Name;
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
    public Guid RoomTypeId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string RoomTypeCode { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public decimal WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; }
}
