using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Clients.DTOs;
using saas.Modules.Clients.Entities;
using saas.Modules.Clients.Services;
using Xunit;

namespace saas.Tests.Modules.Clients;

public class ClientServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private ClientService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Clients.AddRange(
            new Client
            {
                Id = Guid.NewGuid(),
                Name = "Acacia Travel",
                Company = "Acacia",
                Email = "hello@acacia.test",
                Phone = "+27 11 100 2000",
                Country = "South Africa",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new Client
            {
                Id = Guid.NewGuid(),
                Name = "Berlin Explorer",
                Company = "Explorer GmbH",
                Email = "team@explorer.test",
                Phone = "+49 30 555 000",
                Country = "Germany",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        await _db.SaveChangesAsync();

        _service = new ClientService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── GetListAsync ──

    [Fact]
    public async Task GetListAsync_WithSearch_FiltersAcrossFields()
    {
        var result = await _service.GetListAsync("germany");

        Assert.Single(result.Items);
        Assert.Equal("Berlin Explorer", result.Items[0].Name);
    }

    [Fact]
    public async Task GetListAsync_WithNoSearch_ReturnsAll()
    {
        var result = await _service.GetListAsync();

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetListAsync_SearchByCompany_FindsMatch()
    {
        var result = await _service.GetListAsync("explorer gmbh");

        Assert.Single(result.Items);
        Assert.Equal("Berlin Explorer", result.Items[0].Name);
    }

    [Fact]
    public async Task GetListAsync_SearchByPhone_FindsMatch()
    {
        var result = await _service.GetListAsync("+49");

        Assert.Single(result.Items);
        Assert.Equal("Berlin Explorer", result.Items[0].Name);
    }

    [Fact]
    public async Task GetListAsync_SearchByEmail_FindsMatch()
    {
        var result = await _service.GetListAsync("acacia.test");

        Assert.Single(result.Items);
        Assert.Equal("Acacia Travel", result.Items[0].Name);
    }

    [Fact]
    public async Task GetListAsync_SearchNoMatch_ReturnsEmpty()
    {
        var result = await _service.GetListAsync("nonexistent-xyz");

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetListAsync_OrdersByNameAscending()
    {
        var result = await _service.GetListAsync();

        Assert.Equal("Acacia Travel", result.Items[0].Name);
        Assert.Equal("Berlin Explorer", result.Items[1].Name);
    }

    [Fact]
    public async Task GetListAsync_PaginatesCorrectly()
    {
        // Add enough clients to exceed the minimum page size (5)
        for (var i = 0; i < 8; i++)
            _db.Clients.Add(new Client { Name = $"Paginate Client {i}", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var page1 = await _service.GetListAsync(page: 1, pageSize: 5);
        var page2 = await _service.GetListAsync(page: 2, pageSize: 5);

        Assert.Equal(5, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(10, page1.TotalCount);
        Assert.True(page1.HasNextPage);
        Assert.False(page2.HasNextPage);
    }

    [Fact]
    public async Task GetListAsync_PageClampsToMinimum()
    {
        var result = await _service.GetListAsync(page: -5);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.PageIndex);
    }

    [Fact]
    public async Task GetListAsync_PageSizeClampsToRange()
    {
        var result = await _service.GetListAsync(pageSize: 1);

        // pageSize min is 5
        Assert.Equal(2, result.Items.Count);
    }

    // ── EmailExistsAsync ──

    [Fact]
    public async Task EmailExistsAsync_RespectsExcludeId()
    {
        var existing = await _db.Clients.FirstAsync();

        var existsForSame = await _service.EmailExistsAsync(existing.Email!, existing.Id);
        var existsForDifferent = await _service.EmailExistsAsync(existing.Email!);

        Assert.False(existsForSame);
        Assert.True(existsForDifferent);
    }

    [Fact]
    public async Task EmailExistsAsync_CaseInsensitive()
    {
        var exists = await _service.EmailExistsAsync("HELLO@ACACIA.TEST");

        Assert.True(exists);
    }

    [Fact]
    public async Task EmailExistsAsync_TrimsWhitespace()
    {
        var exists = await _service.EmailExistsAsync("  hello@acacia.test  ");

        Assert.True(exists);
    }

    // ── CreateAsync ──

    [Fact]
    public async Task CreateAsync_TrimsAndPersistsValues()
    {
        var dto = new ClientDto
        {
            Name = "  New Client  ",
            Company = "  Blue Dune  ",
            Email = "  info@bluedune.test  ",
            Notes = "  Important account  "
        };

        await _service.CreateAsync(dto);

        var created = await _db.Clients.SingleAsync(c => c.Name == "New Client");
        Assert.Equal("Blue Dune", created.Company);
        Assert.Equal("info@bluedune.test", created.Email);
        Assert.Equal("Important account", created.Notes);
    }

    [Fact]
    public async Task CreateAsync_NormalizesWhitespaceOnlyToNull()
    {
        var dto = new ClientDto
        {
            Name = "Minimal Client",
            Company = "   ",
            Email = "  ",
            Phone = null
        };

        await _service.CreateAsync(dto);

        var created = await _db.Clients.SingleAsync(c => c.Name == "Minimal Client");
        Assert.Null(created.Company);
        Assert.Null(created.Email);
        Assert.Null(created.Phone);
    }

    [Fact]
    public async Task CreateAsync_GeneratesNewId()
    {
        var dto = new ClientDto { Name = "Id Test Client" };

        await _service.CreateAsync(dto);

        var created = await _db.Clients.SingleAsync(c => c.Name == "Id Test Client");
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    // ── GetAsync ──

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsDto()
    {
        var existing = await _db.Clients.FirstAsync();

        var dto = await _service.GetAsync(existing.Id);

        Assert.NotNull(dto);
        Assert.Equal(existing.Name, dto!.Name);
        Assert.Equal(existing.Email, dto.Email);
    }

    [Fact]
    public async Task GetAsync_NonExistentId_ReturnsNull()
    {
        var dto = await _service.GetAsync(Guid.NewGuid());

        Assert.Null(dto);
    }

    // ── GetDetailsAsync ──

    [Fact]
    public async Task GetDetailsAsync_ReturnsCreatedAt()
    {
        var existing = await _db.Clients.FirstAsync();

        var dto = await _service.GetDetailsAsync(existing.Id);

        Assert.NotNull(dto);
        Assert.Equal(existing.CreatedAt, dto!.CreatedAt);
    }

    [Fact]
    public async Task GetDetailsAsync_NonExistentId_ReturnsNull()
    {
        var dto = await _service.GetDetailsAsync(Guid.NewGuid());

        Assert.Null(dto);
    }

    // ── UpdateAsync ──

    [Fact]
    public async Task UpdateAsync_UpdatesFields()
    {
        var existing = await _db.Clients.FirstAsync();
        var dto = new ClientDto
        {
            Name = "Updated Name",
            Company = "Updated Co",
            Email = "updated@example.test",
            Country = "France"
        };

        await _service.UpdateAsync(existing.Id, dto);

        var updated = await _db.Clients.FirstAsync(c => c.Id == existing.Id);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Updated Co", updated.Company);
        Assert.Equal("updated@example.test", updated.Email);
        Assert.Equal("France", updated.Country);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentClient_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(Guid.NewGuid(), new ClientDto { Name = "Missing" }));
    }

    // ── DeleteAsync ──

    [Fact]
    public async Task DeleteAsync_RemovesClient()
    {
        var existing = await _db.Clients.FirstAsync();

        await _service.DeleteAsync(existing.Id);

        Assert.False(await _db.Clients.AnyAsync(c => c.Id == existing.Id));
    }

    [Fact]
    public async Task DeleteAsync_NonExistentClient_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteAsync(Guid.NewGuid()));
    }
}
