using saas.Data;
using saas.Modules.Clients.Entities;

namespace saas.Modules.Portal.Entities;

public enum PortalLinkScope
{
    Full = 0,
    BookingOnly = 1,
    QuoteOnly = 2
}

public class PortalLink : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public PortalLinkScope Scope { get; set; } = PortalLinkScope.Full;
    public Guid? ScopedEntityId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime? LastAccessedAt { get; set; }
    public bool IsRevoked { get; set; }

    public ICollection<PortalSession> Sessions { get; set; } = [];

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
