using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Inventory.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Suppliers;

public class SupplierIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public SupplierIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    // ── Layer 1: Full Page Load ──

    [Fact]
    public async Task SuppliersPage_RendersFullLayout()
    {
        var response = await _client.GetAsync($"/{TenantSlug}/suppliers");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync("#modal-container");
        await response.AssertContainsAsync("Suppliers");
    }

    // ── Layer 2: Partial Isolation ──

    [Fact]
    public async Task SuppliersListPartial_RendersWithoutLayout()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/list");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("table, div.rounded-box");
    }

    [Fact]
    public async Task SuppliersListPartial_RendersExplicitOpenActions()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/list");

        response.AssertSuccess();
        await response.AssertContainsAsync("Open");
        await response.AssertContainsAsync($"href=\"/{TenantSlug}/suppliers/details/");
    }

    [Fact]
    public async Task SuppliersListPartial_WhenMoreThanOnePage_PaginatesResults()
    {
        var prefix = $"PagedSupp-{Guid.NewGuid():N}";

        await using (var db = OpenTenantDb())
        {
            for (var index = 1; index <= 13; index++)
            {
                db.Suppliers.Add(new saas.Modules.Settings.Entities.Supplier
                {
                    Id = Guid.NewGuid(),
                    Name = $"{prefix}-{index:D2}",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddMinutes(index)
                });
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/list?search={prefix}");
        firstPage.AssertSuccess();
        await firstPage.AssertContainsAsync($"{prefix}-01");
        await firstPage.AssertContainsAsync($"{prefix}-12");
        await firstPage.AssertDoesNotContainAsync($"{prefix}-13");
        await firstPage.AssertContainsAsync("Next");

        var secondPage = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/list?search={prefix}&page=2");
        secondPage.AssertSuccess();
        await secondPage.AssertContainsAsync($"{prefix}-13");
        await secondPage.AssertDoesNotContainAsync($"{prefix}-01");
    }

    // ── Layer 3: User Flow ──

    [Fact]
    public async Task SupplierNewPartial_RendersModalForm()
    {
        var response = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/new");

        response.AssertSuccess();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("New Supplier");
    }

    [Fact]
    public async Task CreateSupplier_OnInvalidSubmit_RerendersForm()
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/new");
        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = ""
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
    }

    // ── Layer 4: Database Verification ──

    [Fact]
    public async Task CreateSupplier_OnValidSubmit_PersistsToDatabase()
    {
        var uniqueName = $"Supp-{Guid.NewGuid():N}"[..16];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/new");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["ContactName"] = "Integration Test Contact",
            ["ContactEmail"] = $"{uniqueName.ToLowerInvariant()}@test.local",
            ["ContactPhone"] = "+27 11 999 0000",
            ["Rating"] = "4",
            ["PaymentTerms"] = "Net 30",
            ["IsActive"] = "true"
        });

        response.AssertSuccess();
        response.AssertToast("Supplier created.");

        await using var db = OpenTenantDb();
        var supplier = await db.Suppliers.SingleAsync(s => s.Name == uniqueName);
        Assert.NotEqual(Guid.Empty, supplier.Id);
        Assert.Equal("Integration Test Contact", supplier.ContactName);
        Assert.Equal($"{uniqueName.ToLowerInvariant()}@test.local", supplier.ContactEmail);
        Assert.Equal("+27 11 999 0000", supplier.ContactPhone);
        Assert.Equal(4, supplier.Rating);
        Assert.Equal("Net 30", supplier.PaymentTerms);
        Assert.True(supplier.IsActive);
    }

    [Fact]
    public async Task SupplierDetailsPage_AfterCreate_RendersDetails()
    {
        var supplierId = await SeedSupplierAsync();

        var response = await _client.GetAsync($"/{TenantSlug}/suppliers/details/{supplierId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#main-content");
        await response.AssertElementExistsAsync($"a[href='/{TenantSlug}/suppliers']");
        await response.AssertContainsAsync("Back to Suppliers");
    }

    [Fact]
    public async Task EditSupplier_OnValidSubmit_UpdatesDatabase()
    {
        var supplierId = await SeedSupplierAsync();

        var editForm = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/edit/{supplierId}");
        editForm.AssertSuccess();

        var updatedName = $"Updated-{Guid.NewGuid():N}"[..16];
        var response = await _client.SubmitFormAsync(editForm, "form", new Dictionary<string, string>
        {
            ["Name"] = updatedName,
            ["ContactName"] = "Updated Contact",
            ["Rating"] = "5",
            ["IsActive"] = "true"
        });

        response.AssertSuccess();
        response.AssertToast("Supplier updated.");

        await using var db = OpenTenantDb();
        var supplier = await db.Suppliers.SingleAsync(s => s.Id == supplierId);
        Assert.Equal(updatedName, supplier.Name);
        Assert.Equal("Updated Contact", supplier.ContactName);
        Assert.Equal(5, supplier.Rating);
    }

    [Fact]
    public async Task DeleteSupplier_RemovesFromDatabase()
    {
        var supplierId = await SeedSupplierAsync();

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/delete-confirm/{supplierId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());
        response.AssertSuccess();

        await using var db = OpenTenantDb();
        Assert.False(await db.Suppliers.AnyAsync(s => s.Id == supplierId));
    }

    [Fact]
    public async Task DeleteSupplierConfirm_WhenSupplierReferencedByBooking_ShowsBlockedMessage()
    {
        var supplierId = await SeedReferencedSupplierAsync(linkToBooking: true, linkToInventory: false);

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/delete-confirm/{supplierId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("cannot be deleted yet");
        await response.AssertDoesNotContainAsync("<button type=\"submit\" class=\"btn btn-error\">Delete</button>");
    }

    [Fact]
    public async Task DeleteSupplier_WhenSupplierReferencedByInventory_DoesNotDelete()
    {
        var supplierId = await SeedSupplierAsync();

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/delete-confirm/{supplierId}");
        confirmResponse.AssertSuccess();

        await using (var seedDb = OpenTenantDb())
        {
            seedDb.InventoryItems.Add(new InventoryItem
            {
                Id = Guid.NewGuid(),
                Name = $"Ref-Inv-{Guid.NewGuid():N}"[..16],
                Kind = InventoryItemKind.Hotel,
                BaseCost = 500m,
                SupplierId = supplierId,
                CreatedAt = DateTime.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());
        response.AssertSuccess();
        response.AssertToast("This supplier is referenced by inventory items and cannot be deleted yet.");

        await using var db = OpenTenantDb();
        Assert.True(await db.Suppliers.AnyAsync(s => s.Id == supplierId));
    }

    [Fact]
    public async Task CreateContact_OnValidSubmit_PersistsToDatabase()
    {
        var supplierId = await SeedSupplierAsync();

        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/contacts/new/{supplierId}");
        formResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = "Jane Doe",
            ["Role"] = "Operations Manager",
            ["Email"] = "jane@contact.test",
            ["Phone"] = "+27 82 000 1111",
            ["IsPrimary"] = "true"
        });

        response.AssertSuccess();
        response.AssertToast("Contact added.");

        await using var db = OpenTenantDb();
        var contact = await db.SupplierContacts.SingleAsync(c => c.SupplierId == supplierId && c.Name == "Jane Doe");
        Assert.Equal("Operations Manager", contact.Role);
        Assert.Equal("jane@contact.test", contact.Email);
        Assert.Equal("+27 82 000 1111", contact.Phone);
        Assert.True(contact.IsPrimary);
    }

    [Fact]
    public async Task DeleteContact_RemovesFromDatabase()
    {
        var supplierId = await SeedSupplierAsync();
        var contactId = await SeedContactAsync(supplierId);

        var confirmResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/contacts/delete-confirm/{contactId}");
        confirmResponse.AssertSuccess();

        var response = await _client.SubmitFormAsync(confirmResponse, "form", new Dictionary<string, string>());
        response.AssertSuccess();
        response.AssertToast("Contact removed.");

        await using var db = OpenTenantDb();
        Assert.False(await db.SupplierContacts.AnyAsync(c => c.Id == contactId));
    }

    // ── Access Control ──

    [Fact]
    public async Task SuppliersPage_WhenUnauthenticated_Redirects()
    {
        var publicClient = _fixture.CreateClient();
        var response = await publicClient.GetAsync($"/{TenantSlug}/suppliers");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    // ========== HELPERS ==========

    private async Task<Guid> SeedSupplierAsync()
    {
        var uniqueName = $"Supp-{Guid.NewGuid():N}"[..16];
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/new");
        await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = uniqueName,
            ["ContactName"] = "Seed Contact",
            ["IsActive"] = "true"
        });

        await using var db = OpenTenantDb();
        return (await db.Suppliers.SingleAsync(s => s.Name == uniqueName)).Id;
    }

    private async Task<Guid> SeedContactAsync(Guid supplierId)
    {
        var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/suppliers/contacts/new/{supplierId}");
        await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
        {
            ["Name"] = $"Contact-{Guid.NewGuid():N}"[..16],
            ["Role"] = "Tester"
        });

        await using var db = OpenTenantDb();
        return (await db.SupplierContacts.Where(c => c.SupplierId == supplierId).OrderByDescending(c => c.CreatedAt).FirstAsync()).Id;
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }

    private async Task<Guid> SeedReferencedSupplierAsync(bool linkToBooking, bool linkToInventory)
    {
        var supplierId = Guid.NewGuid();

        await using var db = OpenTenantDb();
        db.Suppliers.Add(new saas.Modules.Settings.Entities.Supplier
        {
            Id = supplierId,
            Name = $"Ref-Supp-{Guid.NewGuid():N}"[..17],
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        if (linkToBooking)
        {
            var client = await db.Clients.OrderBy(c => c.Name).FirstAsync();
            var bookingId = Guid.NewGuid();
            db.Bookings.Add(new Booking
            {
                Id = bookingId,
                BookingRef = $"BK-SUP-{Guid.NewGuid():N}"[..15],
                ClientId = client.Id,
                Status = BookingStatus.Provisional,
                CostCurrencyCode = "USD",
                SellingCurrencyCode = "USD",
                CreatedAt = DateTime.UtcNow
            });
            db.BookingItems.Add(new BookingItem
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                SupplierId = supplierId,
                ServiceName = "Referenced supplier stay",
                CostPrice = 100m,
                SellingPrice = 150m,
                Quantity = 1
            });
        }

        if (linkToInventory)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                Id = Guid.NewGuid(),
                Name = $"Ref-Inv-{Guid.NewGuid():N}"[..16],
                Kind = InventoryItemKind.Hotel,
                BaseCost = 500m,
                SupplierId = supplierId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return supplierId;
    }
}
