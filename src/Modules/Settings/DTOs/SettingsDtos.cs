using System.ComponentModel.DataAnnotations;
using saas.Modules.Settings.Entities;
using saas.Modules.TenantAdmin.Services;

namespace saas.Modules.Settings.DTOs;

public class RoomTypeListItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class RoomTypeDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(0, 999)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class MealPlanListItemDto : RoomTypeListItemDto;

public class MealPlanDto : RoomTypeDto;

public class CurrencyListItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal DefaultMarkup { get; set; }
    public RoundingRule RoundingRule { get; set; }
    public bool IsBaseCurrency { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastManualUpdate { get; set; }
}

public class CurrencyDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(10)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(10)]
    public string? Symbol { get; set; }

    [Range(typeof(decimal), "0.000001", "999999999")]
    public decimal ExchangeRate { get; set; } = 1m;

    [Range(typeof(decimal), "0", "999.99")]
    public decimal DefaultMarkup { get; set; }

    public RoundingRule RoundingRule { get; set; } = RoundingRule.None;
    public bool IsBaseCurrency { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DestinationListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? Region { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class DestinationDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2)]
    public string? CountryCode { get; set; }

    [StringLength(120)]
    public string? CountryName { get; set; }

    [StringLength(100)]
    public string? Region { get; set; }

    [Range(0, 999)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class SupplierListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(120)]
    public string? ContactName { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(320)]
    public string? ContactEmail { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}

public class RateCategoryListItemDto
{
    public Guid Id { get; set; }
    public InventoryType ForType { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Capacity { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class RateCategoryDto
{
    public Guid Id { get; set; }
    public InventoryType ForType { get; set; } = InventoryType.Flight;

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(0, 1000)]
    public int? Capacity { get; set; }

    [Range(0, 999)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class SettingsUsersViewModel
{
    public IReadOnlyList<UserListItem> Users { get; set; } = [];
}

public class SettingsDeleteConfirmDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DeleteUrl { get; set; } = string.Empty;
}

public class RateCategoryGroupDto
{
    public InventoryType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<RateCategoryListItemDto> Items { get; set; } = [];
}
