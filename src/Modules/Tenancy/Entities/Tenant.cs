using saas.Data;
using saas.Modules.Billing.Entities;

namespace saas.Modules.Tenancy.Entities;

public class Tenant : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string? DatabaseName { get; set; }
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;
    public Subscription? ActiveSubscription { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    // Trial support
    public DateTime? TrialEndsAt { get; set; }

    // Custom domain support
    public string? CustomDomain { get; set; }

    // Soft delete support
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? ScheduledDeletionAt { get; set; }

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum TenantStatus
{
    PendingSetup,
    Active,
    Suspended,
    Cancelled
}
