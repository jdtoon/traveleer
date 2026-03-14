using saas.Data;

namespace saas.Modules.Settings.Entities;

public class Supplier : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    // Extended fields for Supplier Management module
    public string? RegistrationNumber { get; set; }
    public string? BankDetails { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal? DefaultCommissionPercentage { get; set; }
    public string? DefaultCurrencyCode { get; set; }
    public int? Rating { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
