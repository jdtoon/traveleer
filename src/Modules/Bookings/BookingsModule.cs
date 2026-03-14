using saas.Modules.Bookings.Services;
using saas.Shared;

namespace saas.Modules.Bookings;

public static class BookingFeatures
{
    public const string Bookings = "bookings";
}

public static class BookingPermissions
{
    public const string BookingsRead = "bookings.read";
    public const string BookingsCreate = "bookings.create";
    public const string BookingsEdit = "bookings.edit";
    public const string BookingsDelete = "bookings.delete";
}

public class BookingsModule : IModule
{
    public string Name => "Bookings";

    public IReadOnlyDictionary<string, string> ControllerViewPaths => new Dictionary<string, string>
    {
        ["Booking"] = "Bookings",
        ["Payment"] = "Bookings",
        ["Document"] = "Bookings"
    };

    public IReadOnlyList<string> PartialViewSearchPaths => ["Booking", "Payment", "Document"];

    public IReadOnlyList<ModuleFeature> Features =>
    [
        new(BookingFeatures.Bookings, "Bookings", "Operational bookings for clients, suppliers, and sold travel services", MinPlanSlug: "starter")
    ];

    public IReadOnlyList<ModulePermission> Permissions =>
    [
        new(BookingPermissions.BookingsRead, "View Bookings", "Bookings", 0),
        new(BookingPermissions.BookingsCreate, "Create Bookings", "Bookings", 1),
        new(BookingPermissions.BookingsEdit, "Edit Bookings", "Bookings", 2),
        new(BookingPermissions.BookingsDelete, "Delete Bookings", "Bookings", 3)
    ];

    public IReadOnlyList<RolePermissionMapping> DefaultRolePermissions =>
    [
        new("Member", BookingPermissions.BookingsRead)
    ];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IBookingVoucherDocumentService, BookingVoucherDocumentService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IDocumentService, DocumentService>();
    }
}
