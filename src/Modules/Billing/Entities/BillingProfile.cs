using saas.Modules.Tenancy.Entities;

namespace saas.Modules.Billing.Entities;

public class BillingProfile
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string? CompanyName { get; set; }
    public string? BillingAddress { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = "ZA";
    public string? VatNumber { get; set; }
    public string? BillingEmail { get; set; }
}
