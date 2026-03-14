using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;
using saas.Modules.Suppliers.DTOs;
using saas.Modules.Suppliers.Entities;
using saas.Modules.Suppliers.Services;
using Xunit;

namespace saas.Tests.Modules.Suppliers;

public class SupplierServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private SupplierService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.Currencies.AddRange(
            new Currency { Code = "ZAR", Name = "Rand", Symbol = "R", ExchangeRate = 1m, IsBaseCurrency = true, CreatedAt = DateTime.UtcNow },
            new Currency { Code = "USD", Name = "Dollar", Symbol = "$", ExchangeRate = 0.055m, IsActive = true, CreatedAt = DateTime.UtcNow });
        _db.Suppliers.Add(new Supplier
        {
            Name = "Safari Lodge",
            ContactName = "John Smith",
            ContactEmail = "john@safari.test",
            ContactPhone = "+27 11 123 4567",
            IsActive = true,
            Rating = 4,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _service = new SupplierService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetListAsync_ReturnsOrderedByName()
    {
        _db.Suppliers.Add(new Supplier { Name = "Alpha Tours", IsActive = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _service.GetListAsync();

        Assert.Equal("Alpha Tours", result[0].Name);
        Assert.Equal("Safari Lodge", result[1].Name);
    }

    [Fact]
    public async Task GetListAsync_WithSearch_FiltersByNameContactOrEmail()
    {
        _db.Suppliers.Add(new Supplier { Name = "Beach Resort", ContactName = "Alice", ContactEmail = "alice@beach.test", IsActive = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var byName = await _service.GetListAsync("safari");
        Assert.Single(byName);
        Assert.Equal("Safari Lodge", byName[0].Name);

        var byContact = await _service.GetListAsync("alice");
        Assert.Single(byContact);
        Assert.Equal("Beach Resort", byContact[0].Name);

        var byEmail = await _service.GetListAsync("beach.test");
        Assert.Single(byEmail);
    }

    [Fact]
    public async Task CreateAsync_TrimsAndNormalizesFields()
    {
        await _service.CreateAsync(new SupplierFormDto
        {
            Name = "  Mountain Lodge  ",
            ContactName = "  Jane  ",
            ContactEmail = "  jane@mountain.test  ",
            Website = "  https://mountain.test  ",
            Address = "  123 Mountain Rd  ",
            PaymentTerms = "  Net 30  ",
            IsActive = true
        });

        var created = await _db.Suppliers.SingleAsync(x => x.Name == "Mountain Lodge");
        Assert.Equal("Jane", created.ContactName);
        Assert.Equal("jane@mountain.test", created.ContactEmail);
        Assert.Equal("https://mountain.test", created.Website);
        Assert.Equal("123 Mountain Rd", created.Address);
        Assert.Equal("Net 30", created.PaymentTerms);
    }

    [Fact]
    public async Task CreateAsync_NullableFieldsSetToNullWhenWhitespace()
    {
        await _service.CreateAsync(new SupplierFormDto
        {
            Name = "Empty Fields",
            ContactName = "  ",
            ContactEmail = "  ",
            Notes = "",
            IsActive = true
        });

        var created = await _db.Suppliers.SingleAsync(x => x.Name == "Empty Fields");
        Assert.Null(created.ContactName);
        Assert.Null(created.ContactEmail);
        Assert.Null(created.Notes);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesAllFields()
    {
        var supplier = await _db.Suppliers.FirstAsync();

        await _service.UpdateAsync(supplier.Id, new SupplierFormDto
        {
            Name = "Updated Lodge",
            ContactName = "Updated Contact",
            ContactEmail = "updated@lodge.test",
            Rating = 5,
            DefaultCommissionPercentage = 15.5m,
            IsActive = false
        });

        var updated = await _db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplier.Id);
        Assert.Equal("Updated Lodge", updated.Name);
        Assert.Equal("Updated Contact", updated.ContactName);
        Assert.Equal("updated@lodge.test", updated.ContactEmail);
        Assert.Equal(5, updated.Rating);
        Assert.Equal(15.5m, updated.DefaultCommissionPercentage);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSupplierAndCascadesContacts()
    {
        var supplier = await _db.Suppliers.FirstAsync();
        _db.SupplierContacts.AddRange(
            new SupplierContact { SupplierId = supplier.Id, Name = "Contact 1", CreatedAt = DateTime.UtcNow },
            new SupplierContact { SupplierId = supplier.Id, Name = "Contact 2", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await _service.DeleteAsync(supplier.Id);

        Assert.False(await _db.Suppliers.AnyAsync(s => s.Id == supplier.Id));
        Assert.False(await _db.SupplierContacts.AnyAsync(c => c.SupplierId == supplier.Id));
    }

    [Fact]
    public async Task GetDetailsAsync_ReturnsContactsAndBookingItemCount()
    {
        var supplier = await _db.Suppliers.FirstAsync();
        _db.SupplierContacts.AddRange(
            new SupplierContact { SupplierId = supplier.Id, Name = "Primary", IsPrimary = true, CreatedAt = DateTime.UtcNow },
            new SupplierContact { SupplierId = supplier.Id, Name = "Secondary", IsPrimary = false, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var details = await _service.GetDetailsAsync(supplier.Id);

        Assert.NotNull(details);
        Assert.Equal(supplier.Name, details!.Name);
        Assert.Equal(2, details.Contacts.Count);
        Assert.Equal("Primary", details.Contacts[0].Name); // Primary first
        Assert.Equal(0, details.BookingItemCount);
    }

    [Fact]
    public async Task GetDetailsAsync_WhenNotFound_ReturnsNull()
    {
        var result = await _service.GetDetailsAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateEmptyAsync_ReturnsIsActiveAndCurrencyOptions()
    {
        var empty = await _service.CreateEmptyAsync();

        Assert.True(empty.IsActive);
        Assert.Contains("USD", empty.CurrencyOptions);
    }

    [Fact]
    public async Task GetFormAsync_ReturnsAllFieldsAndCurrencyOptions()
    {
        var supplier = await _db.Suppliers.FirstAsync();

        var form = await _service.GetFormAsync(supplier.Id);

        Assert.NotNull(form);
        Assert.Equal(supplier.Name, form!.Name);
        Assert.Equal(supplier.ContactName, form.ContactName);
        Assert.NotEmpty(form.CurrencyOptions);
    }

    // ========== CONTACT TESTS ==========

    [Fact]
    public async Task CreateContactAsync_TrimsAndNormalizesFields()
    {
        var supplier = await _db.Suppliers.FirstAsync();

        await _service.CreateContactAsync(new SupplierContactFormDto
        {
            SupplierId = supplier.Id,
            Name = "  Alice Wonder  ",
            Role = "  Manager  ",
            Email = "  alice@test.com  ",
            Phone = "  +1234  ",
            IsPrimary = true
        });

        var contact = await _db.SupplierContacts.SingleAsync(c => c.Name == "Alice Wonder");
        Assert.Equal("Manager", contact.Role);
        Assert.Equal("alice@test.com", contact.Email);
        Assert.Equal("+1234", contact.Phone);
        Assert.True(contact.IsPrimary);
    }

    [Fact]
    public async Task UpdateContactAsync_UpdatesAllFields()
    {
        var supplier = await _db.Suppliers.FirstAsync();
        _db.SupplierContacts.Add(new SupplierContact
        {
            SupplierId = supplier.Id, Name = "Original", Role = "Staff",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var contact = await _db.SupplierContacts.FirstAsync();

        await _service.UpdateContactAsync(contact.Id, new SupplierContactFormDto
        {
            SupplierId = supplier.Id,
            Name = "Updated Name",
            Role = "Director",
            Email = "updated@test.com",
            Phone = "+9999",
            IsPrimary = true
        });

        var updated = await _db.SupplierContacts.AsNoTracking().FirstAsync(c => c.Id == contact.Id);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Director", updated.Role);
        Assert.Equal("updated@test.com", updated.Email);
        Assert.True(updated.IsPrimary);
    }

    [Fact]
    public async Task DeleteContactAsync_RemovesContact()
    {
        var supplier = await _db.Suppliers.FirstAsync();
        _db.SupplierContacts.Add(new SupplierContact
        {
            SupplierId = supplier.Id, Name = "Deletable",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var contact = await _db.SupplierContacts.SingleAsync(c => c.Name == "Deletable");

        await _service.DeleteContactAsync(contact.Id);

        Assert.False(await _db.SupplierContacts.AnyAsync(c => c.Id == contact.Id));
    }

    [Fact]
    public async Task GetContactsAsync_ReturnsPrimaryFirstThenAlphabetical()
    {
        var supplier = await _db.Suppliers.FirstAsync();
        _db.SupplierContacts.AddRange(
            new SupplierContact { SupplierId = supplier.Id, Name = "Zara", IsPrimary = false, CreatedAt = DateTime.UtcNow },
            new SupplierContact { SupplierId = supplier.Id, Name = "Alice", IsPrimary = false, CreatedAt = DateTime.UtcNow },
            new SupplierContact { SupplierId = supplier.Id, Name = "Main Contact", IsPrimary = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var contacts = await _service.GetContactsAsync(supplier.Id);

        Assert.Equal("Main Contact", contacts[0].Name);
        Assert.Equal("Alice", contacts[1].Name);
        Assert.Equal("Zara", contacts[2].Name);
    }
}
