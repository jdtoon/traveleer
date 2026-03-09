using System.ComponentModel.DataAnnotations;
using saas.Modules.Inventory.Entities;

namespace saas.Modules.Inventory.DTOs;

public class InventoryListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public InventoryItemKind Kind { get; set; }
    public string? Description { get; set; }
    public decimal BaseCost { get; set; }
    public string? ImageUrl { get; set; }
    public string? Address { get; set; }
    public int? Rating { get; set; }
    public Guid? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
}

public class InventoryDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public InventoryItemKind Kind { get; set; } = InventoryItemKind.Hotel;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal BaseCost { get; set; }

    [Url(ErrorMessage = "Enter a valid URL.")]
    [StringLength(500)]
    public string? ImageUrl { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int? Rating { get; set; }

    public Guid? DestinationId { get; set; }
    public Guid? SupplierId { get; set; }
    public List<InventoryOptionDto> DestinationOptions { get; set; } = [];
    public List<InventoryOptionDto> SupplierOptions { get; set; } = [];
}

public class InventoryOptionDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class InventoryFilterDto
{
    public string? Type { get; set; }
    public string? Search { get; set; }
}
