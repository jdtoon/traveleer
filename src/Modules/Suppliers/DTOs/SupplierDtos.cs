using System.ComponentModel.DataAnnotations;

namespace saas.Modules.Suppliers.DTOs;

public class SupplierListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public int? Rating { get; set; }
    public bool IsActive { get; set; }
}

public class SupplierFormDto
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

    [StringLength(100)]
    public string? RegistrationNumber { get; set; }

    [StringLength(500)]
    public string? BankDetails { get; set; }

    [StringLength(200)]
    public string? PaymentTerms { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Commission must be between 0 and 100.")]
    public decimal? DefaultCommissionPercentage { get; set; }

    [StringLength(10)]
    public string? DefaultCurrencyCode { get; set; }

    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int? Rating { get; set; }

    [StringLength(500)]
    [Url(ErrorMessage = "Enter a valid URL.")]
    public string? Website { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    public List<string> CurrencyOptions { get; set; } = [];
}

public class SupplierDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? BankDetails { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal? DefaultCommissionPercentage { get; set; }
    public string? DefaultCurrencyCode { get; set; }
    public int? Rating { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SupplierContactListItemDto> Contacts { get; set; } = [];
    public int BookingItemCount { get; set; }
}

public class SupplierContactListItemDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
}

public class SupplierContactFormDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Role { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(320)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    public bool IsPrimary { get; set; }
}

public class SupplierDeleteConfirmDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DeleteUrl { get; set; } = string.Empty;
}
