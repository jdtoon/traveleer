using Microsoft.EntityFrameworkCore;
using saas.Data.Core;

namespace saas.Modules.Billing.Services;

/// <summary>
/// Generates sequential invoice numbers in the format INV-{YEAR}-{SEQUENCE}.
/// </summary>
public class InvoiceGenerator
{
    private readonly CoreDbContext _coreDb;

    public InvoiceGenerator(CoreDbContext coreDb)
    {
        _coreDb = coreDb;
    }

    public async Task<Invoice> GenerateAsync(Guid tenantId, Guid subscriptionId,
        decimal amount, string currency)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"INV-{year}-";

        var lastInvoice = await _coreDb.Invoices
            .Where(i => i.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(i => i.InvoiceNumber)
            .FirstOrDefaultAsync();

        var sequence = 1;
        if (lastInvoice is not null)
        {
            var lastNum = lastInvoice.InvoiceNumber.Split('-').Last();
            if (int.TryParse(lastNum, out var parsed))
                sequence = parsed + 1;
        }

        var invoice = new Invoice
        {
            TenantId = tenantId,
            SubscriptionId = subscriptionId,
            InvoiceNumber = $"{prefix}{sequence:D4}",
            Subtotal = amount,
            Total = amount,
            Currency = currency,
            Status = InvoiceStatus.Issued,
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow, // Due immediately for auto-billing
        };

        _coreDb.Invoices.Add(invoice);
        await _coreDb.SaveChangesAsync();

        return invoice;
    }
}
