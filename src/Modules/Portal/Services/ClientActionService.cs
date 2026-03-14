using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Portal.DTOs;
using saas.Modules.Portal.Entities;

namespace saas.Modules.Portal.Services;

public interface IClientActionService
{
    Task<List<ClientActionListItemDto>> GetActionsAsync(ClientActionStatus? statusFilter = null);
    Task<ClientAction> SubmitActionAsync(SubmitClientActionDto dto, Guid clientId, Guid? portalSessionId);
    Task AcknowledgeAsync(Guid id, string userId);
    Task DismissAsync(Guid id, string userId);
    Task<List<ClientActionListItemDto>> GetActionsByEntityAsync(string entityType, Guid entityId);
}

public class ClientActionService : IClientActionService
{
    private readonly TenantDbContext _db;

    public ClientActionService(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<List<ClientActionListItemDto>> GetActionsAsync(ClientActionStatus? statusFilter = null)
    {
        var query = _db.ClientActions.AsNoTracking()
            .Include(a => a.Client)
            .AsQueryable();

        if (statusFilter.HasValue)
            query = query.Where(a => a.Status == statusFilter.Value);

        var actions = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();
        return actions.Select(MapToListItem).ToList();
    }

    public async Task<ClientAction> SubmitActionAsync(SubmitClientActionDto dto, Guid clientId, Guid? portalSessionId)
    {
        var action = new ClientAction
        {
            ClientId = clientId,
            PortalSessionId = portalSessionId,
            ActionType = dto.ActionType,
            EntityType = dto.EntityType,
            EntityId = dto.EntityId,
            Notes = dto.Notes,
            Status = ClientActionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.ClientActions.Add(action);
        await _db.SaveChangesAsync();
        return action;
    }

    public async Task AcknowledgeAsync(Guid id, string userId)
    {
        var action = await _db.ClientActions.FindAsync(id);
        if (action is null || action.Status != ClientActionStatus.Pending) return;

        action.Status = ClientActionStatus.Acknowledged;
        action.AcknowledgedByUserId = userId;
        action.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DismissAsync(Guid id, string userId)
    {
        var action = await _db.ClientActions.FindAsync(id);
        if (action is null || action.Status != ClientActionStatus.Pending) return;

        action.Status = ClientActionStatus.Dismissed;
        action.AcknowledgedByUserId = userId;
        action.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<ClientActionListItemDto>> GetActionsByEntityAsync(string entityType, Guid entityId)
    {
        var actions = await _db.ClientActions.AsNoTracking()
            .Include(a => a.Client)
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return actions.Select(MapToListItem).ToList();
    }

    private static string? GetEntityRef(ClientAction a)
    {
        return a.EntityType switch
        {
            "Quote" => $"Quote {a.EntityId.ToString()[..8]}",
            "Booking" => $"Booking {a.EntityId.ToString()[..8]}",
            "Itinerary" => $"Itinerary {a.EntityId.ToString()[..8]}",
            _ => null
        };
    }

    private static ClientActionListItemDto MapToListItem(ClientAction a) => new()
    {
        Id = a.Id,
        ClientId = a.ClientId,
        ClientName = a.Client?.Name ?? "Unknown",
        ActionType = a.ActionType,
        EntityType = a.EntityType,
        EntityId = a.EntityId,
        EntityRef = GetEntityRef(a),
        Notes = a.Notes,
        Status = a.Status,
        AcknowledgedByUserId = a.AcknowledgedByUserId,
        AcknowledgedAt = a.AcknowledgedAt,
        CreatedAt = a.CreatedAt
    };
}
