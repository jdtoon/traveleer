using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;
using saas.Modules.Settings.Services;
using saas.Shared;

namespace saas.Modules.Settings;

public static class SettingsFeatures
{
    public const string Settings = "settings";
}

public static class SettingsPermissions
{
    public const string SettingsRead = "masterdata.read";
    public const string SettingsEdit = "masterdata.edit";
}

public class SettingsModule : IModule
{
    public string Name => "Settings";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Settings"] = "Settings"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Settings"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(SettingsFeatures.Settings, "Settings", "Reference data and agency master settings for a tenant", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(SettingsPermissions.SettingsRead, "View Settings", "Settings", 0),
        new(SettingsPermissions.SettingsEdit, "Edit Settings", "Settings", 1)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", SettingsPermissions.SettingsRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISettingsService, SettingsService>();
    }

    public async Task SeedTenantAsync(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<TenantDbContext>();
        if (await db.RoomTypes.AnyAsync())
            return;

        db.RoomTypes.AddRange(
            CreateRoomType("SGL", "Single Room", 10),
            CreateRoomType("DBL", "Double Room", 20),
            CreateRoomType("TWN", "Twin Room", 30),
            CreateRoomType("TRP", "Triple Room", 40),
            CreateRoomType("QAD", "Quad Room", 50),
            CreateRoomType("STE", "Suite", 60),
            CreateRoomType("FAM", "Family Room", 70));

        db.MealPlans.AddRange(
            CreateMealPlan("RO", "Room Only", 10),
            CreateMealPlan("BB", "Bed & Breakfast", 20),
            CreateMealPlan("HB", "Half Board", 30),
            CreateMealPlan("FB", "Full Board", 40),
            CreateMealPlan("AI", "All Inclusive", 50));

        db.Currencies.AddRange(
            CreateCurrency("ZAR", "South African Rand", "R", 1m, true, 0m),
            CreateCurrency("USD", "US Dollar", "$", 0.055m, false, 12m),
            CreateCurrency("EUR", "Euro", "EUR", 0.051m, false, 12m),
            CreateCurrency("GBP", "British Pound", "GBP", 0.043m, false, 12m),
            CreateCurrency("SAR", "Saudi Riyal", "SAR", 0.207m, false, 10m),
            CreateCurrency("AED", "UAE Dirham", "AED", 0.202m, false, 10m),
            CreateCurrency("INR", "Indian Rupee", "INR", 4.56m, false, 8m),
            CreateCurrency("PKR", "Pakistani Rupee", "PKR", 15.4m, false, 8m),
            CreateCurrency("EGP", "Egyptian Pound", "EGP", 2.7m, false, 8m));

        db.RateCategories.AddRange(
            CreateRateCategory(InventoryType.Flight, "ECO", "Economy Class", 10),
            CreateRateCategory(InventoryType.Flight, "PRM", "Premium Economy", 20),
            CreateRateCategory(InventoryType.Flight, "BUS", "Business Class", 30),
            CreateRateCategory(InventoryType.Flight, "FST", "First Class", 40),
            CreateRateCategory(InventoryType.Excursion, "ADT", "Adult", 10),
            CreateRateCategory(InventoryType.Excursion, "CHD", "Child", 20),
            CreateRateCategory(InventoryType.Excursion, "INF", "Infant", 30),
            CreateRateCategory(InventoryType.Excursion, "SNR", "Senior", 40),
            CreateRateCategory(InventoryType.Excursion, "PVT", "Private", 50),
            CreateRateCategory(InventoryType.Transfer, "SDN", "Sedan", 10, 3),
            CreateRateCategory(InventoryType.Transfer, "SUV", "SUV", 20, 5),
            CreateRateCategory(InventoryType.Transfer, "VAN", "Van", 30, 12),
            CreateRateCategory(InventoryType.Transfer, "MNB", "Mini Bus", 40, 18),
            CreateRateCategory(InventoryType.Transfer, "BUS", "Coach", 50, 40),
            CreateRateCategory(InventoryType.Transfer, "LUX", "Luxury Vehicle", 60, 3),
            CreateRateCategory(InventoryType.Visa, "STD", "Standard", 10),
            CreateRateCategory(InventoryType.Visa, "EXP", "Express", 20),
            CreateRateCategory(InventoryType.Visa, "URG", "Urgent", 30),
            CreateRateCategory(InventoryType.Visa, "VIP", "VIP", 40));

        await db.SaveChangesAsync();
    }

    public async Task SeedDemoDataAsync(IServiceProvider scopedServices)
    {
        var db = scopedServices.GetRequiredService<TenantDbContext>();
        if (!await db.Destinations.AnyAsync())
        {
            db.Destinations.AddRange(
                CreateDestination("Makkah", "SA", "Saudi Arabia", "Middle East", 10),
                CreateDestination("Madinah", "SA", "Saudi Arabia", "Middle East", 20),
                CreateDestination("Jeddah", "SA", "Saudi Arabia", "Middle East", 30),
                CreateDestination("Dubai", "AE", "United Arab Emirates", "Middle East", 40),
                CreateDestination("Abu Dhabi", "AE", "United Arab Emirates", "Middle East", 50),
                CreateDestination("Cairo", "EG", "Egypt", "North Africa", 60),
                CreateDestination("Cape Town", "ZA", "South Africa", "Africa", 70),
                CreateDestination("Istanbul", "TR", "Turkey", "Europe", 80),
                CreateDestination("London", "GB", "United Kingdom", "Europe", 90),
                CreateDestination("Paris", "FR", "France", "Europe", 100));
        }

        if (!await db.Suppliers.AnyAsync())
        {
            db.Suppliers.AddRange(
                CreateSupplier("Al Haram Hotels", "Reservations Desk", "rooms@alharam.test", "+966 11 555 0100"),
                CreateSupplier("Saudi Airlines GSA", "Aviation Team", "sales@saudia-gsa.test", "+966 11 555 0200"),
                CreateSupplier("Hajj Tours Transport", "Ground Ops", "ops@hajjtours.test", "+966 11 555 0300"),
                CreateSupplier("Ziyarah Excursions", "Excursions Team", "bookings@ziyarah.test", "+966 11 555 0400"),
                CreateSupplier("Saudi Visa Services", "Visa Desk", "support@saudivisa.test", "+966 11 555 0500"));
        }

        await db.SaveChangesAsync();
    }

    private static RoomType CreateRoomType(string code, string name, int sortOrder) => new()
    {
        Code = code,
        Name = name,
        SortOrder = sortOrder,
        IsActive = true
    };

    private static MealPlan CreateMealPlan(string code, string name, int sortOrder) => new()
    {
        Code = code,
        Name = name,
        SortOrder = sortOrder,
        IsActive = true
    };

    private static Currency CreateCurrency(string code, string name, string symbol, decimal exchangeRate, bool isBase, decimal defaultMarkup) => new()
    {
        Code = code,
        Name = name,
        Symbol = symbol,
        ExchangeRate = exchangeRate,
        IsBaseCurrency = isBase,
        DefaultMarkup = defaultMarkup,
        IsActive = true,
        LastManualUpdate = DateTime.UtcNow
    };

    private static Destination CreateDestination(string name, string countryCode, string countryName, string region, int sortOrder) => new()
    {
        Name = name,
        CountryCode = countryCode,
        CountryName = countryName,
        Region = region,
        SortOrder = sortOrder,
        IsActive = true
    };

    private static Supplier CreateSupplier(string name, string contactName, string email, string phone) => new()
    {
        Name = name,
        ContactName = contactName,
        ContactEmail = email,
        ContactPhone = phone,
        IsActive = true
    };

    private static RateCategory CreateRateCategory(InventoryType type, string code, string name, int sortOrder, int? capacity = null) => new()
    {
        ForType = type,
        Code = code,
        Name = name,
        SortOrder = sortOrder,
        Capacity = capacity,
        IsActive = true
    };
}
