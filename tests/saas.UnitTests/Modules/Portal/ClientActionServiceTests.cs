using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Clients.Entities;
using saas.Modules.Portal.DTOs;
using saas.Modules.Portal.Entities;
using saas.Modules.Portal.Services;
using Xunit;

namespace saas.Tests.Modules.Portal;

public class ClientActionServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private ClientActionService _service = null!;
    private Client _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _client = new Client
        {
            Name = "Test Client",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _db.Clients.Add(_client);
        await _db.SaveChangesAsync();
        _service = new ClientActionService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ========== SUBMIT ACTION ==========

    [Fact]
    public async Task SubmitActionAsync_CreatesAction()
    {
        var dto = new SubmitClientActionDto
        {
            ActionType = ClientActionType.AcceptQuote,
            EntityType = "Quote",
            EntityId = Guid.NewGuid(),
            Notes = "Looks great!"
        };

        var action = await _service.SubmitActionAsync(dto, _client.Id, null);

        Assert.NotNull(action);
        Assert.Equal(ClientActionType.AcceptQuote, action.ActionType);
        Assert.Equal("Quote", action.EntityType);
        Assert.Equal("Looks great!", action.Notes);
        Assert.Equal(ClientActionStatus.Pending, action.Status);
        Assert.Equal(_client.Id, action.ClientId);
    }

    [Fact]
    public async Task SubmitActionAsync_WithPortalSession_StoresSessionId()
    {
        var sessionId = Guid.NewGuid();
        var link = new PortalLink
        {
            ClientId = _client.Id,
            Token = "test-token",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        };
        _db.PortalLinks.Add(link);
        var session = new PortalSession
        {
            Id = sessionId,
            PortalLinkId = link.Id,
            ClientId = _client.Id,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };
        _db.PortalSessions.Add(session);
        await _db.SaveChangesAsync();

        var dto = new SubmitClientActionDto
        {
            ActionType = ClientActionType.SubmitFeedback,
            EntityType = "Booking",
            EntityId = Guid.NewGuid()
        };

        var action = await _service.SubmitActionAsync(dto, _client.Id, sessionId);

        Assert.Equal(sessionId, action.PortalSessionId);
    }

    [Theory]
    [InlineData(ClientActionType.AcceptQuote)]
    [InlineData(ClientActionType.DeclineQuote)]
    [InlineData(ClientActionType.RequestChange)]
    [InlineData(ClientActionType.ApproveItinerary)]
    [InlineData(ClientActionType.SubmitFeedback)]
    public async Task SubmitActionAsync_AllActionTypes_Succeed(ClientActionType actionType)
    {
        var dto = new SubmitClientActionDto
        {
            ActionType = actionType,
            EntityType = "Quote",
            EntityId = Guid.NewGuid()
        };

        var action = await _service.SubmitActionAsync(dto, _client.Id, null);

        Assert.Equal(actionType, action.ActionType);
        Assert.Equal(ClientActionStatus.Pending, action.Status);
    }

    // ========== GET ACTIONS ==========

    [Fact]
    public async Task GetActionsAsync_ReturnsAllActions()
    {
        await SeedActionAsync(ClientActionType.AcceptQuote);
        await SeedActionAsync(ClientActionType.DeclineQuote);

        var actions = await _service.GetActionsAsync();

        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public async Task GetActionsAsync_FilterByStatus_ReturnsPendingOnly()
    {
        var action1 = await SeedActionAsync(ClientActionType.AcceptQuote);
        await SeedActionAsync(ClientActionType.DeclineQuote);
        await _service.AcknowledgeAsync(action1.Id, "user-1");

        var pending = await _service.GetActionsAsync(ClientActionStatus.Pending);

        Assert.Single(pending);
        Assert.Equal(ClientActionStatus.Pending, pending[0].Status);
    }

    [Fact]
    public async Task GetActionsAsync_OrderedByCreatedAtDesc()
    {
        // Seed actions with explicit timestamps to avoid timing issues
        var entityId1 = Guid.NewGuid();
        var entityId2 = Guid.NewGuid();
        var olderAction = new ClientAction
        {
            ClientId = _client.Id,
            ActionType = ClientActionType.AcceptQuote,
            EntityType = "Quote",
            EntityId = entityId1,
            Status = ClientActionStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        var newerAction = new ClientAction
        {
            ClientId = _client.Id,
            ActionType = ClientActionType.DeclineQuote,
            EntityType = "Quote",
            EntityId = entityId2,
            Status = ClientActionStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.ClientActions.AddRange(olderAction, newerAction);
        await _db.SaveChangesAsync();

        var actions = await _service.GetActionsAsync();

        Assert.Equal(newerAction.Id, actions[0].Id);
        Assert.Equal(olderAction.Id, actions[1].Id);
    }

    [Fact]
    public async Task GetActionsAsync_IncludesClientName()
    {
        await SeedActionAsync(ClientActionType.AcceptQuote);

        var actions = await _service.GetActionsAsync();

        Assert.Equal("Test Client", actions[0].ClientName);
    }

    // ========== ACKNOWLEDGE ==========

    [Fact]
    public async Task AcknowledgeAsync_SetsStatusAndUser()
    {
        var action = await SeedActionAsync(ClientActionType.AcceptQuote);

        await _service.AcknowledgeAsync(action.Id, "admin-user");

        var updated = await _db.ClientActions.FindAsync(action.Id);
        Assert.Equal(ClientActionStatus.Acknowledged, updated!.Status);
        Assert.Equal("admin-user", updated.AcknowledgedByUserId);
        Assert.NotNull(updated.AcknowledgedAt);
    }

    [Fact]
    public async Task AcknowledgeAsync_AlreadyAcknowledged_NoChange()
    {
        var action = await SeedActionAsync(ClientActionType.AcceptQuote);
        await _service.AcknowledgeAsync(action.Id, "user-1");

        // Try again — should not change
        await _service.AcknowledgeAsync(action.Id, "user-2");

        var updated = await _db.ClientActions.FindAsync(action.Id);
        Assert.Equal("user-1", updated!.AcknowledgedByUserId);
    }

    [Fact]
    public async Task AcknowledgeAsync_NonExistentId_DoesNotThrow()
    {
        await _service.AcknowledgeAsync(Guid.NewGuid(), "user-1"); // should not throw
    }

    // ========== DISMISS ==========

    [Fact]
    public async Task DismissAsync_SetsStatusAndUser()
    {
        var action = await SeedActionAsync(ClientActionType.RequestChange);

        await _service.DismissAsync(action.Id, "admin-user");

        var updated = await _db.ClientActions.FindAsync(action.Id);
        Assert.Equal(ClientActionStatus.Dismissed, updated!.Status);
        Assert.Equal("admin-user", updated.AcknowledgedByUserId);
        Assert.NotNull(updated.AcknowledgedAt);
    }

    [Fact]
    public async Task DismissAsync_AlreadyDismissed_NoChange()
    {
        var action = await SeedActionAsync(ClientActionType.RequestChange);
        await _service.DismissAsync(action.Id, "user-1");

        await _service.DismissAsync(action.Id, "user-2");

        var updated = await _db.ClientActions.FindAsync(action.Id);
        Assert.Equal("user-1", updated!.AcknowledgedByUserId);
    }

    // ========== GET BY ENTITY ==========

    [Fact]
    public async Task GetActionsByEntityAsync_ReturnsMatchingActions()
    {
        var entityId = Guid.NewGuid();
        await SeedActionAsync(ClientActionType.AcceptQuote, "Quote", entityId);
        await SeedActionAsync(ClientActionType.RequestChange, "Quote", entityId);
        await SeedActionAsync(ClientActionType.SubmitFeedback, "Booking", Guid.NewGuid());

        var actions = await _service.GetActionsByEntityAsync("Quote", entityId);

        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public async Task GetActionsByEntityAsync_NoMatches_ReturnsEmpty()
    {
        var actions = await _service.GetActionsByEntityAsync("Quote", Guid.NewGuid());
        Assert.Empty(actions);
    }

    // ========== DTO MAPPING ==========

    [Fact]
    public async Task ActionListItemDto_ActionLabel_MapsCorrectly()
    {
        await SeedActionAsync(ClientActionType.AcceptQuote);
        var actions = await _service.GetActionsAsync();

        Assert.Equal("Quote Accepted", actions[0].ActionLabel);
    }

    // ========== HELPERS ==========

    private async Task<ClientAction> SeedActionAsync(
        ClientActionType actionType = ClientActionType.AcceptQuote,
        string entityType = "Quote",
        Guid? entityId = null)
    {
        var dto = new SubmitClientActionDto
        {
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId ?? Guid.NewGuid()
        };
        return await _service.SubmitActionAsync(dto, _client.Id, null);
    }
}
