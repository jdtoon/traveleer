using System.ComponentModel.DataAnnotations;
using saas.Modules.RateCards.Entities;

namespace saas.Modules.RateCards.DTOs;

public class RateCardOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class RateCardTemplateOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemTemplate { get; set; }
    public int SeasonCount { get; set; }
}

public class RateCardListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InventoryItemName { get; set; } = string.Empty;
    public string? DestinationName { get; set; }
    public string ContractCurrencyCode { get; set; } = "USD";
    public RateCardStatus Status { get; set; }
    public int SeasonCount { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RateCardFormDto
{
    [Required(ErrorMessage = "Rate card name is required.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Hotel inventory item is required.")]
    public Guid? InventoryItemId { get; set; }

    public Guid? TemplateId { get; set; }

    public Guid? DefaultMealPlanId { get; set; }

    [Required(ErrorMessage = "Contract currency is required.")]
    [StringLength(10)]
    public string ContractCurrencyCode { get; set; } = "USD";

    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public List<RateCardOptionDto> InventoryOptions { get; set; } = [];
    public List<RateCardTemplateOptionDto> TemplateOptions { get; set; } = [];
    public List<RateCardOptionDto> MealPlanOptions { get; set; } = [];
    public List<string> CurrencyOptions { get; set; } = [];
}

public class RateCardRoomTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class RateCardRateCellDto
{
    public Guid? RoomRateId { get; set; }
    public Guid RoomTypeId { get; set; }
    public string RoomTypeCode { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public decimal WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; } = true;
}

public class RateCardSeasonEditorDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int SortOrder { get; set; }
    public bool IsBlackout { get; set; }
    public string? Notes { get; set; }
    public List<RateCardRateCellDto> Rates { get; set; } = [];
}

public class RateCardDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public RateCardStatus Status { get; set; }
    public Guid InventoryItemId { get; set; }
    public string InventoryItemName { get; set; } = string.Empty;
    public string? DestinationName { get; set; }
    public string ContractCurrencyCode { get; set; } = "USD";
    public string? DefaultMealPlanName { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int AvailableTemplateCount { get; set; }
    public List<RateCardRoomTypeDto> RoomTypes { get; set; } = [];
    public List<RateCardSeasonEditorDto> Seasons { get; set; } = [];
}

public class SaveRateCardTemplateDto
{
    public Guid RateCardId { get; set; }

    [Required(ErrorMessage = "Template name is required.")]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public class RateCardJsonExportDto
{
    public string ExportVersion { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; }
    public string? ExportedBy { get; set; }
    public RateCardJsonExportCardDto RateCard { get; set; } = new();
}

public class RateCardJsonExportCardDto
{
    public string Name { get; set; } = string.Empty;
    public RateCardStatus Status { get; set; }
    public string InventoryItemName { get; set; } = string.Empty;
    public string? DestinationName { get; set; }
    public string ContractCurrencyCode { get; set; } = "USD";
    public string? DefaultMealPlanCode { get; set; }
    public string? DefaultMealPlanName { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Notes { get; set; }
    public List<RateCardJsonExportSeasonDto> Seasons { get; set; } = [];
}

public class RateCardJsonExportSeasonDto
{
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int SortOrder { get; set; }
    public bool IsBlackout { get; set; }
    public string? Notes { get; set; }
    public List<RateCardJsonExportRateDto> Rates { get; set; } = [];
}

public class RateCardJsonExportRateDto
{
    public string RoomTypeCode { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public decimal WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; }
}

public class RateCardCsvImportFormDto
{
    public Guid RateCardId { get; set; }
    public string RateCardName { get; set; } = string.Empty;
}

public class RateCardCsvImportPreviewDto
{
    public Guid RateCardId { get; set; }
    public string RateCardName { get; set; } = string.Empty;
    public string? ImportToken { get; set; }
    public string? ErrorMessage { get; set; }
    public int ValidRowCount { get; set; }
    public int InvalidRowCount { get; set; }
    public int TotalRowCount => Rows.Count;
    public bool CanImport => !string.IsNullOrWhiteSpace(ImportToken) && ValidRowCount > 0;
    public List<string> Warnings { get; set; } = [];
    public List<RateCardCsvImportPreviewRowDto> Rows { get; set; } = [];
}

public class RateCardCsvImportPreviewRowDto
{
    public int LineNumber { get; set; }
    public Guid? SeasonId { get; set; }
    public Guid? RoomTypeId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string RoomTypeCode { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public string RawWeekdayRate { get; set; } = string.Empty;
    public string RawWeekendRate { get; set; } = string.Empty;
    public string RawIsIncluded { get; set; } = string.Empty;
    public decimal? WeekdayRate { get; set; }
    public decimal? WeekendRate { get; set; }
    public bool IsIncluded { get; set; } = true;
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RateCardCsvImportExecuteDto
{
    [Required]
    public string ImportToken { get; set; } = string.Empty;
}

public class RateCardCsvImportResultDto
{
    public Guid RateCardId { get; set; }
    public string RateCardName { get; set; } = string.Empty;
    public int ImportedRowCount { get; set; }
}

public class RateSeasonFormDto
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }

    [Required(ErrorMessage = "Season name is required.")]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
    public bool IsBlackout { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class RateCardRateUpdateDto
{
    public Guid RateCardId { get; set; }
    public Guid RateSeasonId { get; set; }
    public Guid RoomTypeId { get; set; }

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Weekday rate must be zero or more.")]
    public decimal WeekdayRate { get; set; }

    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "Weekend rate must be zero or more.")]
    public decimal? WeekendRate { get; set; }

    public bool IsIncluded { get; set; } = true;
}
