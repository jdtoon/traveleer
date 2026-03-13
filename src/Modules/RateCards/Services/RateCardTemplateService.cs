using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.DTOs;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.Services;

public interface IRateCardTemplateService
{
    Task EnsureSystemTemplatesAsync();
    Task<List<RateCardTemplateOptionDto>> GetOptionsAsync(InventoryItemKind? kind = null);
    Task<List<TemplateSeasonDefinition>> GetSeasonDefinitionsAsync(Guid templateId);
    Task CreateFromRateCardAsync(Guid rateCardId, string name, string? description);
}

public class RateCardTemplateService : IRateCardTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TenantDbContext _db;

    public RateCardTemplateService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task EnsureSystemTemplatesAsync()
    {
        var existingSystemNames = await _db.RateCardTemplates
            .AsNoTracking()
            .Where(x => x.IsSystemTemplate)
            .Select(x => x.Name)
            .ToListAsync();

        var existingSet = existingSystemNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var templates = GetSystemTemplates()
            .Where(x => !existingSet.Contains(x.Name))
            .ToList();

        if (templates.Count == 0)
        {
            return;
        }

        _db.RateCardTemplates.AddRange(templates);
        await _db.SaveChangesAsync();
    }

    public async Task<List<RateCardTemplateOptionDto>> GetOptionsAsync(InventoryItemKind? kind = null)
    {
        await EnsureSystemTemplatesAsync();

        var query = _db.RateCardTemplates
            .AsNoTracking();

        if (kind.HasValue)
        {
            query = query.Where(x => x.ForKind == kind.Value);
        }

        var templates = await query
            .OrderByDescending(x => x.IsSystemTemplate)
            .ThenBy(x => x.ForKind)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return templates
            .Select(x => new RateCardTemplateOptionDto
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                ForKind = x.ForKind,
                IsSystemTemplate = x.IsSystemTemplate,
                SeasonCount = JsonSerializer.Deserialize<List<TemplateSeasonDefinition>>(x.SeasonsJson, JsonOptions)?.Count ?? 0
            })
            .ToList();
    }

    public async Task<List<TemplateSeasonDefinition>> GetSeasonDefinitionsAsync(Guid templateId)
    {
        var template = await _db.RateCardTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId)
            ?? throw new InvalidOperationException("Rate card template was not found.");

        return JsonSerializer.Deserialize<List<TemplateSeasonDefinition>>(template.SeasonsJson, JsonOptions)
            ?.OrderBy(x => x.SortOrder)
            .ToList() ?? [];
    }

    public async Task CreateFromRateCardAsync(Guid rateCardId, string name, string? description)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new InvalidOperationException("Template name is required.");
        }

        var rateCard = await _db.RateCards
            .AsNoTracking()
            .Include(x => x.InventoryItem)
            .Include(x => x.Seasons)
            .FirstOrDefaultAsync(x => x.Id == rateCardId)
            ?? throw new InvalidOperationException("Rate card was not found.");

        var inventoryKind = rateCard.InventoryItem?.Kind ?? InventoryItemKind.Hotel;
        var exists = await _db.RateCardTemplates.AnyAsync(x => x.Name == trimmedName && x.ForKind == inventoryKind);
        if (exists)
        {
            throw new InvalidOperationException("A template with this name already exists.");
        }

        if (rateCard.Seasons.Count == 0)
        {
            throw new InvalidOperationException("Add at least one season before saving a template.");
        }

        var definitions = rateCard.Seasons
            .OrderBy(x => x.SortOrder)
            .Select(x => new TemplateSeasonDefinition
            {
                Name = x.Name,
                MonthStart = x.StartDate.Month,
                DayStart = x.StartDate.Day,
                MonthEnd = x.EndDate.Month,
                DayEnd = x.EndDate.Day,
                SortOrder = x.SortOrder,
                IsBlackout = x.IsBlackout,
                Notes = x.Notes
            })
            .ToList();

        _db.RateCardTemplates.Add(new RateCardTemplate
        {
            Name = trimmedName,
            ForKind = inventoryKind,
            Description = Normalize(description),
            SeasonsJson = JsonSerializer.Serialize(definitions, JsonOptions),
            IsSystemTemplate = false
        });

        await _db.SaveChangesAsync();
    }

    public static List<RateSeasonFormDto> BuildSeasonForms(List<TemplateSeasonDefinition> definitions, int targetYear)
    {
        var forms = new List<RateSeasonFormDto>();

        foreach (var definition in definitions.OrderBy(x => x.SortOrder))
        {
            var startDate = new DateOnly(targetYear, 1, 1);
            var endDate = new DateOnly(targetYear, 12, 31);

            if (definition.UsesDatePattern)
            {
                var startYear = targetYear;
                var endYear = targetYear;
                if (definition.MonthEnd < definition.MonthStart ||
                    (definition.MonthEnd == definition.MonthStart && definition.DayEnd < definition.DayStart))
                {
                    endYear++;
                }

                startDate = new DateOnly(startYear, definition.MonthStart!.Value, Math.Min(definition.DayStart!.Value, DateTime.DaysInMonth(startYear, definition.MonthStart.Value)));
                endDate = new DateOnly(endYear, definition.MonthEnd!.Value, Math.Min(definition.DayEnd!.Value, DateTime.DaysInMonth(endYear, definition.MonthEnd.Value)));
            }

            forms.Add(new RateSeasonFormDto
            {
                Name = definition.Name,
                StartDate = startDate,
                EndDate = endDate,
                IsBlackout = definition.IsBlackout,
                Notes = definition.Notes
            });
        }

        return forms;
    }

    private static List<RateCardTemplate> GetSystemTemplates()
    {
        var templates = new List<RateCardTemplate>();

        templates.AddRange(new[]
        {
            new RateCardTemplate
            {
                Name = "Year-Round Fixed",
                ForKind = InventoryItemKind.Hotel,
                Description = "One full-year season for stable supplier contracts.",
                SeasonsJson = JsonSerializer.Serialize(new List<TemplateSeasonDefinition>
                {
                    new() { Name = "Standard Season", MonthStart = 1, DayStart = 1, MonthEnd = 12, DayEnd = 31, SortOrder = 10 }
                }, JsonOptions),
                IsSystemTemplate = true
            },
            new RateCardTemplate
            {
                Name = "Three-Season Standard",
                ForKind = InventoryItemKind.Hotel,
                Description = "Low, shoulder, and peak windows for a classic annual contract.",
                SeasonsJson = JsonSerializer.Serialize(new List<TemplateSeasonDefinition>
                {
                    new() { Name = "Low Season", MonthStart = 1, DayStart = 1, MonthEnd = 4, DayEnd = 30, SortOrder = 10 },
                    new() { Name = "Shoulder Season", MonthStart = 5, DayStart = 1, MonthEnd = 8, DayEnd = 31, SortOrder = 20 },
                    new() { Name = "Peak Season", MonthStart = 9, DayStart = 1, MonthEnd = 12, DayEnd = 31, SortOrder = 30 }
                }, JsonOptions),
                IsSystemTemplate = true
            },
            new RateCardTemplate
            {
                Name = "Pilgrimage High Season",
                ForKind = InventoryItemKind.Hotel,
                Description = "Shoulder and pilgrimage windows with a dedicated blackout period.",
                SeasonsJson = JsonSerializer.Serialize(new List<TemplateSeasonDefinition>
                {
                    new() { Name = "Shoulder Window", MonthStart = 1, DayStart = 1, MonthEnd = 5, DayEnd = 31, SortOrder = 10 },
                    new() { Name = "Pilgrimage Peak", MonthStart = 6, DayStart = 1, MonthEnd = 9, DayEnd = 30, SortOrder = 20 },
                    new() { Name = "Stop Sell", MonthStart = 10, DayStart = 1, MonthEnd = 10, DayEnd = 15, SortOrder = 30, IsBlackout = true, Notes = "Pause sales while supplier allocation is reconfirmed." },
                    new() { Name = "Late Year", MonthStart = 10, DayStart = 16, MonthEnd = 12, DayEnd = 31, SortOrder = 40 }
                }, JsonOptions),
                IsSystemTemplate = true
            }
        });

        foreach (var kind in new[] { InventoryItemKind.Flight, InventoryItemKind.Excursion, InventoryItemKind.Transfer, InventoryItemKind.Visa, InventoryItemKind.Other })
        {
            templates.Add(new RateCardTemplate
            {
                Name = "Year-Round Fixed",
                ForKind = kind,
                Description = "One full-year season for fixed supplier pricing.",
                SeasonsJson = JsonSerializer.Serialize(new List<TemplateSeasonDefinition>
                {
                    new() { Name = "Standard Window", MonthStart = 1, DayStart = 1, MonthEnd = 12, DayEnd = 31, SortOrder = 10 }
                }, JsonOptions),
                IsSystemTemplate = true
            });

            templates.Add(new RateCardTemplate
            {
                Name = "Peak And Shoulder",
                ForKind = kind,
                Description = "Shoulder and peak windows for demand-led travel products.",
                SeasonsJson = JsonSerializer.Serialize(new List<TemplateSeasonDefinition>
                {
                    new() { Name = "Shoulder Window", MonthStart = 1, DayStart = 1, MonthEnd = 5, DayEnd = 31, SortOrder = 10 },
                    new() { Name = "Peak Window", MonthStart = 6, DayStart = 1, MonthEnd = 9, DayEnd = 30, SortOrder = 20 },
                    new() { Name = "Late Window", MonthStart = 10, DayStart = 1, MonthEnd = 12, DayEnd = 31, SortOrder = 30 }
                }, JsonOptions),
                IsSystemTemplate = true
            });
        }

        return templates;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
