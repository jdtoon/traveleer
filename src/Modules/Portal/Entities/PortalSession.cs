using saas.Modules.Clients.Entities;

namespace saas.Modules.Portal.Entities;

public class PortalSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PortalLinkId { get; set; }
    public PortalLink? PortalLink { get; set; }
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? IpAddress { get; set; }
}
