namespace saas.Modules.Bookings.Events;

public static class BookingEvents
{
    public const string Refresh = "bookings.refresh";
    public const string ItemsRefresh = "bookings.items.refresh";
    public const string PaymentsRefresh = "bookings.payments.refresh";
    public const string SupplierPaymentsRefresh = "bookings.supplier-payments.refresh";
    public const string DocumentsRefresh = "bookings.documents.refresh";
    public const string ClientDocumentsRefresh = "clients.documents.refresh";
}
