using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using saas.Modules.Bookings.Entities;
using saas.Modules.Branding.Entities;

namespace saas.Modules.Bookings.Services;

public interface IBookingVoucherDocumentService
{
    byte[] Generate(Booking booking, BookingItem item, BrandingSettings branding, string agencyName);
}

public class BookingVoucherDocumentService : IBookingVoucherDocumentService
{
    public BookingVoucherDocumentService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(Booking booking, BookingItem item, BrandingSettings branding, string agencyName)
    {
        var displayAgencyName = string.IsNullOrWhiteSpace(branding.AgencyName) ? agencyName : branding.AgencyName.Trim();
        var supplierName = string.IsNullOrWhiteSpace(item.Supplier?.Name) ? "Supplier" : item.Supplier.Name;
        var leadGuest = string.IsNullOrWhiteSpace(booking.LeadGuestName) ? "Not set" : booking.LeadGuestName;
        var travelWindow = FormatTravelWindow(item.ServiceDate, item.EndDate);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(displayAgencyName).SemiBold().FontSize(18);
                            if (!string.IsNullOrWhiteSpace(branding.PublicContactEmail) || !string.IsNullOrWhiteSpace(branding.ContactPhone))
                            {
                                left.Item().Text($"{branding.PublicContactEmail} {branding.ContactPhone}".Trim())
                                    .FontSize(10)
                                    .FontColor(Colors.Grey.Darken2);
                            }
                        });

                        row.ConstantItem(220).AlignRight().Column(right =>
                        {
                            right.Item().Text("SERVICE VOUCHER").SemiBold().FontSize(14);
                            right.Item().Text($"Voucher No: {item.VoucherNumber}").FontSize(10).FontColor(Colors.Grey.Darken2);
                            right.Item().Text($"Booking Ref: {booking.BookingRef}").FontSize(10).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(16).Column(column =>
                {
                    column.Spacing(14);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("TO").SemiBold();
                            left.Item().Text(supplierName);
                            if (!string.IsNullOrWhiteSpace(item.Supplier?.ContactEmail))
                            {
                                left.Item().Text(item.Supplier.ContactEmail).FontSize(10).FontColor(Colors.Grey.Darken2);
                            }
                            if (!string.IsNullOrWhiteSpace(item.Supplier?.ContactPhone))
                            {
                                left.Item().Text(item.Supplier.ContactPhone).FontSize(10).FontColor(Colors.Grey.Darken2);
                            }
                        });

                        row.RelativeItem().Column(right =>
                        {
                            right.Item().Text("GUEST DETAILS").SemiBold();
                            right.Item().Text($"Lead guest: {leadGuest}");
                            if (!string.IsNullOrWhiteSpace(booking.LeadGuestNationality))
                            {
                                right.Item().Text($"Nationality: {booking.LeadGuestNationality}");
                            }
                            right.Item().Text($"Guests: {booking.Pax}");
                        });
                    });

                    column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    column.Item().Text("SERVICE DETAILS").SemiBold();
                    column.Item().Text($"Service: {item.ServiceName} ({item.ServiceKind})");
                    column.Item().Text($"Dates: {travelWindow}");
                    if (item.Nights.HasValue)
                    {
                        column.Item().Text($"Nights: {item.Nights.Value}");
                    }
                    if (item.Quantity > 1)
                    {
                        column.Item().Text($"Quantity: {item.Quantity}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.SupplierReference))
                    {
                        column.Item().Text($"Supplier Ref: {item.SupplierReference}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.Description))
                    {
                        column.Item().Text($"Description: {item.Description}");
                    }

                    column.Item().Text($"Payment: Direct billing to {displayAgencyName}")
                        .FontColor(Colors.Grey.Darken2);

                    if (!string.IsNullOrWhiteSpace(booking.SpecialRequests))
                    {
                        column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        column.Item().Text("SPECIAL REQUESTS").SemiBold();
                        column.Item().Text(booking.SpecialRequests);
                    }
                });

                page.Footer().AlignCenter().Text(branding.PdfFooterText ?? string.Empty)
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            });
        });

        return document.GeneratePdf();
    }

    private static string FormatTravelWindow(DateOnly? startDate, DateOnly? endDate)
    {
        if (!startDate.HasValue && !endDate.HasValue)
        {
            return "To be confirmed";
        }

        if (startDate.HasValue && endDate.HasValue)
        {
            return $"{startDate.Value:dd MMM yyyy} - {endDate.Value:dd MMM yyyy}";
        }

        return startDate?.ToString("dd MMM yyyy") ?? endDate?.ToString("dd MMM yyyy") ?? "To be confirmed";
    }
}
