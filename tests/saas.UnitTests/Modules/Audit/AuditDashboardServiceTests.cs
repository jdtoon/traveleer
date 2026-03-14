using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Audit;
using saas.Modules.Audit.Entities;
using saas.Modules.Audit.Services;
using Xunit;

namespace saas.UnitTests.Modules.Audit;

public class AuditDashboardServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AuditDbContext _db = null!;
    private AuditDashboardService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AuditDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _service = new AuditDashboardService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AuditEntry CreateEntry(string tenantSlug, string entityType, string action,
        string? userEmail = "user@test.com", string? oldValues = null, string? newValues = null,
        DateTime? timestamp = null)
    {
        return new AuditEntry
        {
            TenantSlug = tenantSlug,
            EntityType = entityType,
            EntityId = Guid.NewGuid().ToString(),
            Action = action,
            UserEmail = userEmail,
            OldValues = oldValues,
            NewValues = newValues,
            Timestamp = timestamp ?? DateTime.UtcNow,
            IpAddress = "127.0.0.1"
        };
    }

    [Fact]
    public async Task GetListAsync_FiltersByTenantSlug()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created"),
            CreateEntry("demo", "Client", "Updated"),
            CreateEntry("other-tenant", "Booking", "Deleted")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, null, null, null, null, 1);

        Assert.Equal(2, result.Entries.Items.Count);
        Assert.All(result.Entries.Items, e => Assert.NotEqual("other-tenant", e.EntityType == "Booking" && e.Action == "Deleted" ? "other-tenant" : "demo"));
    }

    [Fact]
    public async Task GetListAsync_FiltersByEntityType()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created"),
            CreateEntry("demo", "Client", "Created"),
            CreateEntry("demo", "Booking", "Updated")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", "Booking", null, null, null, null, 1);

        Assert.Equal(2, result.Entries.Items.Count);
        Assert.All(result.Entries.Items, e => Assert.Equal("Booking", e.EntityType));
    }

    [Fact]
    public async Task GetListAsync_FiltersByAction()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created"),
            CreateEntry("demo", "Client", "Updated"),
            CreateEntry("demo", "Booking", "Deleted")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, "Created", null, null, null, 1);

        Assert.Single(result.Entries.Items);
        Assert.Equal("Created", result.Entries.Items[0].Action);
    }

    [Fact]
    public async Task GetListAsync_FiltersByUser()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created", userEmail: "admin@test.com"),
            CreateEntry("demo", "Client", "Updated", userEmail: "user@test.com")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, null, "admin@test.com", null, null, 1);

        Assert.Single(result.Entries.Items);
        Assert.Equal("admin@test.com", result.Entries.Items[0].UserEmail);
    }

    [Fact]
    public async Task GetListAsync_FiltersByDateRange()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created", timestamp: new DateTime(2025, 1, 10)),
            CreateEntry("demo", "Client", "Updated", timestamp: new DateTime(2025, 1, 20)),
            CreateEntry("demo", "Booking", "Deleted", timestamp: new DateTime(2025, 2, 5))
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, null, null, "2025-01-15", "2025-01-25", 1);

        Assert.Single(result.Entries.Items);
        Assert.Equal("Client", result.Entries.Items[0].EntityType);
    }

    [Fact]
    public async Task GetListAsync_OrdersByTimestampDescending()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created", timestamp: new DateTime(2025, 1, 1)),
            CreateEntry("demo", "Client", "Updated", timestamp: new DateTime(2025, 3, 1)),
            CreateEntry("demo", "Quote", "Deleted", timestamp: new DateTime(2025, 2, 1))
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, null, null, null, null, 1);

        Assert.Equal("Client", result.Entries.Items[0].EntityType);
        Assert.Equal("Quote", result.Entries.Items[1].EntityType);
        Assert.Equal("Booking", result.Entries.Items[2].EntityType);
    }

    [Fact]
    public async Task GetListAsync_PopulatesDistinctDropdowns()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created", userEmail: "admin@test.com"),
            CreateEntry("demo", "Client", "Updated", userEmail: "user@test.com"),
            CreateEntry("demo", "Booking", "Deleted", userEmail: "admin@test.com")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, null, null, null, null, 1);

        Assert.Equal(["Booking", "Client"], result.DistinctEntityTypes);
        Assert.Equal(["Created", "Deleted", "Updated"], result.DistinctActions);
        Assert.Equal(["admin@test.com", "user@test.com"], result.DistinctUsers);
    }

    [Fact]
    public async Task GetListAsync_HasChangesReflectsValues()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created", newValues: """{"Name":"Test"}"""),
            CreateEntry("demo", "Client", "Deleted")
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync("demo", null, null, null, null, null, 1);

        var booking = result.Entries.Items.First(e => e.EntityType == "Booking");
        var client = result.Entries.Items.First(e => e.EntityType == "Client");
        Assert.True(booking.HasChanges);
        Assert.False(client.HasChanges);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsNullForWrongTenant()
    {
        var entry = CreateEntry("other-tenant", "Booking", "Created", newValues: """{"Name":"Test"}""");
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync();

        var detail = await _service.GetDetailAsync("demo", entry.Id);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsDetailWithFieldChanges()
    {
        var entry = CreateEntry("demo", "Booking", "Updated",
            oldValues: """{"Name":"Old Name","Status":"Draft"}""",
            newValues: """{"Name":"New Name","Status":"Draft"}""");
        entry.AffectedColumns = "Name";
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync();

        var detail = await _service.GetDetailAsync("demo", entry.Id);

        Assert.NotNull(detail);
        Assert.Equal("Booking", detail.EntityType);
        Assert.Equal("Updated", detail.Action);
        Assert.Contains("Name", detail.AffectedColumns);
        Assert.Equal(2, detail.Changes.Count);

        var nameChange = detail.Changes.First(c => c.Field == "Name");
        Assert.Equal("Old Name", nameChange.OldValue);
        Assert.Equal("New Name", nameChange.NewValue);
        Assert.True(nameChange.IsChanged);

        var statusChange = detail.Changes.First(c => c.Field == "Status");
        Assert.Equal("Draft", statusChange.OldValue);
        Assert.Equal("Draft", statusChange.NewValue);
        Assert.False(statusChange.IsChanged);
    }

    [Fact]
    public async Task GetDetailAsync_HandlesCreatedEntryWithOnlyNewValues()
    {
        var entry = CreateEntry("demo", "Client", "Created",
            newValues: """{"Name":"New Client","Email":"test@test.com"}""");
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync();

        var detail = await _service.GetDetailAsync("demo", entry.Id);

        Assert.NotNull(detail);
        Assert.Equal(2, detail.Changes.Count);
        Assert.All(detail.Changes, c =>
        {
            Assert.Null(c.OldValue);
            Assert.NotNull(c.NewValue);
            Assert.True(c.IsChanged);
        });
    }

    [Fact]
    public async Task GetDetailAsync_HandlesDeletedEntryWithOnlyOldValues()
    {
        var entry = CreateEntry("demo", "Client", "Deleted",
            oldValues: """{"Name":"Deleted Client","Email":"test@test.com"}""");
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync();

        var detail = await _service.GetDetailAsync("demo", entry.Id);

        Assert.NotNull(detail);
        Assert.Equal(2, detail.Changes.Count);
        Assert.All(detail.Changes, c =>
        {
            Assert.NotNull(c.OldValue);
            Assert.Null(c.NewValue);
            Assert.True(c.IsChanged);
        });
    }

    [Fact]
    public async Task GetDetailAsync_HandlesInvalidJsonGracefully()
    {
        var entry = CreateEntry("demo", "Booking", "Updated",
            oldValues: "not-valid-json", newValues: """{"Name":"Test"}""");
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync();

        var detail = await _service.GetDetailAsync("demo", entry.Id);

        Assert.NotNull(detail);
        // Old values parse failure results in empty dict, so only new values fields shown
        Assert.Single(detail.Changes);
        Assert.Equal("Name", detail.Changes[0].Field);
    }

    [Fact]
    public async Task GetDistinctEntityTypesAsync_OnlyReturnsForTenant()
    {
        _db.AuditEntries.AddRange(
            CreateEntry("demo", "Booking", "Created"),
            CreateEntry("other", "Invoice", "Created")
        );
        await _db.SaveChangesAsync();

        var types = await _service.GetDistinctEntityTypesAsync("demo");

        Assert.Single(types);
        Assert.Equal("Booking", types[0]);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsNullForNonExistentId()
    {
        var detail = await _service.GetDetailAsync("demo", 99999);
        Assert.Null(detail);
    }
}
