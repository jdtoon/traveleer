using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Shared;

namespace saas.Modules.Billing.Services;

public interface IInvoiceEngine
{
    Task<Invoice> GenerateSubscriptionInvoiceAsync(Guid tenantId, DateTime? periodStart = null, DateTime? periodEnd = null);
    Task<Invoice> GenerateOneOffInvoiceAsync(Guid tenantId, string description, decimal amount);
    Task<Invoice> GenerateProrationInvoiceAsync(Guid tenantId, string description, List<InvoiceLineItem> lineItems);
    Task FinalizeInvoiceAsync(Guid invoiceId);
    Task VoidInvoiceAsync(Guid invoiceId);
    Task<string> GenerateInvoiceNumberAsync();
}

public class InvoiceEngine : IInvoiceEngine
{
    private readonly CoreDbContext _db;
    private readonly BillingOptions _options;
    private readonly ICreditService _creditService;
    private readonly IDiscountService _discountService;
    private readonly ILogger<InvoiceEngine> _logger;

    public InvoiceEngine(
        CoreDbContext db,
        IOptions<BillingOptions> options,
        ICreditService creditService,
        IDiscountService discountService,
        ILogger<InvoiceEngine> logger)
    {
        _db = db;
        _options = options.Value;
        _creditService = creditService;
        _discountService = discountService;
        _logger = logger;
    }

    public async Task<Invoice> GenerateSubscriptionInvoiceAsync(Guid tenantId, DateTime? periodStart = null, DateTime? periodEnd = null)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Plan)
            .ThenInclude(p => p.PricingTiers)
            .Include(t => t.ActiveSubscription)
            .Include(t => t.BillingProfile)
            .FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        var sub = tenant.ActiveSubscription
            ?? throw new InvalidOperationException("No active subscription");

        var plan = tenant.Plan;
        var start = periodStart ?? DateTime.UtcNow;
        var end = periodEnd ?? (sub.BillingCycle == BillingCycle.Annual ? start.AddYears(1) : start.AddMonths(1));

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubscriptionId = sub.Id,
            InvoiceNumber = await GenerateInvoiceNumberAsync(),
            Status = InvoiceStatus.Draft,
            Currency = plan.Currency ?? "ZAR",
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(_options.Invoice.PaymentTermDays),
            BillingPeriodStart = start,
            BillingPeriodEnd = end,
            CreatedAt = DateTime.UtcNow
        };

        SnapshotCompanyDetails(invoice, tenant);
        _db.Invoices.Add(invoice);

        var lineItems = new List<InvoiceLineItem>();
        int sortOrder = 0;

        // 1. Base plan charge
        var basePrice = sub.BillingCycle == BillingCycle.Annual
            ? (plan.AnnualPrice ?? plan.MonthlyPrice * 12)
            : plan.MonthlyPrice;

        lineItems.Add(new InvoiceLineItem
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Type = LineItemType.Subscription,
            Description = $"{plan.Name} ({sub.BillingCycle})",
            Quantity = 1,
            UnitPrice = basePrice,
            Amount = basePrice,
            SortOrder = sortOrder++
        });

        // 2. Per-seat charges (if applicable)
        if (_options.Features.PerSeatBilling
            && (plan.BillingModel == BillingModel.PerSeat || plan.BillingModel == BillingModel.Hybrid)
            && sub.Quantity > (plan.IncludedSeats ?? 0))
        {
            var extraSeats = sub.Quantity - (plan.IncludedSeats ?? 0);
            var seatPrice = sub.BillingCycle == BillingCycle.Annual
                ? (plan.PerSeatAnnualPrice ?? 0)
                : (plan.PerSeatMonthlyPrice ?? 0);

            if (plan.PricingTiers.Count > 0)
                seatPrice = CalculateTieredSeatPrice(plan, sub.Quantity) / sub.Quantity;

            if (extraSeats > 0 && seatPrice > 0)
            {
                lineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Type = LineItemType.Seat,
                    Description = $"Additional seats ({extraSeats} × {seatPrice:C})",
                    Quantity = extraSeats,
                    UnitPrice = seatPrice,
                    Amount = extraSeats * seatPrice,
                    SortOrder = sortOrder++
                });
            }
        }

        // 3. Add-on charges
        if (_options.Features.AddOns)
        {
            var activeAddOns = await _db.TenantAddOns
                .Where(ta => ta.TenantId == tenantId && ta.DeactivatedAt == null)
                .Include(ta => ta.AddOn)
                .ToListAsync();

            foreach (var ta in activeAddOns.Where(a => a.AddOn.BillingInterval != AddOnInterval.OneOff))
            {
                lineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Type = LineItemType.AddOn,
                    Description = ta.AddOn.Name,
                    Quantity = ta.Quantity,
                    UnitPrice = ta.AddOn.Price,
                    Amount = ta.Quantity * ta.AddOn.Price,
                    AddOnId = ta.AddOnId,
                    SortOrder = sortOrder++
                });
            }
        }

        // 4. Setup fee (first invoice only)
        if (_options.Features.SetupFees && plan.SetupFee > 0)
        {
            var hasExistingInvoice = await _db.Invoices
                .AnyAsync(i => i.TenantId == tenantId && i.Id != invoice.Id);
            if (!hasExistingInvoice)
            {
                lineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Type = LineItemType.SetupFee,
                    Description = "One-time setup fee",
                    Quantity = 1,
                    UnitPrice = plan.SetupFee.Value,
                    Amount = plan.SetupFee.Value,
                    SortOrder = sortOrder++
                });
            }
        }

        // Calculate subtotal
        var subtotal = lineItems.Sum(l => l.Amount);
        invoice.Subtotal = subtotal;

        // 5. Apply discounts
        if (_options.Features.Discounts)
        {
            var discountAmount = await _discountService.CalculateDiscountAsync(tenantId, subtotal);
            if (discountAmount > 0)
            {
                invoice.DiscountAmount = discountAmount;
                lineItems.Add(new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Type = LineItemType.Discount,
                    Description = "Discount",
                    Quantity = 1,
                    UnitPrice = -discountAmount,
                    Amount = -discountAmount,
                    SortOrder = sortOrder++
                });
            }
        }

        // 6. Calculate tax
        var taxableAmount = subtotal - invoice.DiscountAmount;
        var taxRate = _options.Tax.Rate;
        invoice.TaxRate = taxRate;

        decimal taxAmount;
        if (_options.Tax.Included)
        {
            // VAT-inclusive: tax is already in the price
            taxAmount = Math.Round(taxableAmount * taxRate / (1 + taxRate), 2);
        }
        else
        {
            // VAT-exclusive: add tax on top
            taxAmount = Math.Round(taxableAmount * taxRate, 2);
        }

        invoice.TaxAmount = taxAmount;

        if (taxAmount > 0)
        {
            lineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Type = LineItemType.Tax,
                Description = $"{_options.Tax.Label} ({taxRate:P0}){(_options.Tax.Included ? " (included)" : "")}",
                Quantity = 1,
                UnitPrice = _options.Tax.Included ? 0 : taxAmount,
                Amount = _options.Tax.Included ? 0 : taxAmount,
                SortOrder = sortOrder++
            });
        }

        // Calculate total before credits
        var total = _options.Tax.Included
            ? taxableAmount
            : taxableAmount + taxAmount;

        // 7. Apply credits
        invoice.Total = total;
        _db.InvoiceLineItems.AddRange(lineItems);
        await _db.SaveChangesAsync();

        await _creditService.ApplyCreditsToInvoiceAsync(tenantId, invoice);
        await _db.SaveChangesAsync();

        // Decrement discount cycles
        if (_options.Features.Discounts)
            await _discountService.DecrementCyclesAsync(tenantId);

        _logger.LogInformation("Generated subscription invoice {Number} for tenant {TenantId}: {Total:C}",
            invoice.InvoiceNumber, tenantId, invoice.Total);

        return invoice;
    }

    public async Task<Invoice> GenerateOneOffInvoiceAsync(Guid tenantId, string description, decimal amount)
    {
        var tenant = await _db.Tenants
            .Include(t => t.BillingProfile)
            .FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = await GenerateInvoiceNumberAsync(),
            Status = InvoiceStatus.Draft,
            Currency = "ZAR",
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(_options.Invoice.PaymentTermDays),
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        SnapshotCompanyDetails(invoice, tenant);
        _db.Invoices.Add(invoice);

        var lineItem = new InvoiceLineItem
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Type = LineItemType.OneOff,
            Description = description,
            Quantity = 1,
            UnitPrice = amount,
            Amount = amount,
            SortOrder = 0
        };
        _db.InvoiceLineItems.Add(lineItem);

        // Tax
        var taxRate = _options.Tax.Rate;
        decimal taxAmount;
        if (_options.Tax.Included)
            taxAmount = Math.Round(amount * taxRate / (1 + taxRate), 2);
        else
            taxAmount = Math.Round(amount * taxRate, 2);

        invoice.Subtotal = amount;
        invoice.TaxAmount = taxAmount;
        invoice.TaxRate = taxRate;
        invoice.Total = _options.Tax.Included ? amount : amount + taxAmount;

        if (!_options.Tax.Included && taxAmount > 0)
        {
            _db.InvoiceLineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Type = LineItemType.Tax,
                Description = $"{_options.Tax.Label} ({taxRate:P0})",
                Quantity = 1,
                UnitPrice = taxAmount,
                Amount = taxAmount,
                SortOrder = 1
            });
        }

        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<Invoice> GenerateProrationInvoiceAsync(Guid tenantId, string description, List<InvoiceLineItem> lineItems)
    {
        var tenant = await _db.Tenants
            .Include(t => t.BillingProfile)
            .FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = await GenerateInvoiceNumberAsync(),
            Status = InvoiceStatus.Draft,
            Currency = "ZAR",
            IssuedDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        SnapshotCompanyDetails(invoice, tenant);
        _db.Invoices.Add(invoice);

        foreach (var li in lineItems)
        {
            li.InvoiceId = invoice.Id;
            if (li.Id == Guid.Empty) li.Id = Guid.NewGuid();
        }
        _db.InvoiceLineItems.AddRange(lineItems);

        var subtotal = lineItems.Sum(l => l.Amount);
        invoice.Subtotal = subtotal;
        invoice.Total = subtotal;

        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task FinalizeInvoiceAsync(Guid invoiceId)
    {
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (invoice is null) return;

        invoice.Status = InvoiceStatus.Issued;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Finalized invoice {Number}", invoice.InvoiceNumber);
    }

    public async Task VoidInvoiceAsync(Guid invoiceId)
    {
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (invoice is null) return;

        invoice.Status = InvoiceStatus.Cancelled;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Voided invoice {Number}", invoice.InvoiceNumber);
    }

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = _options.Invoice.Prefix;
        var pattern = $"{prefix}-{year}-";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            var lastInvoice = await _db.Invoices
                .Where(i => i.InvoiceNumber.StartsWith(pattern))
                .OrderByDescending(i => i.InvoiceNumber)
                .FirstOrDefaultAsync();

            var sequence = 1;
            if (lastInvoice is not null)
            {
                var parts = lastInvoice.InvoiceNumber.Split('-');
                if (parts.Length >= 3 && int.TryParse(parts[^1], out var parsed))
                    sequence = parsed + 1;
            }

            var number = $"{prefix}-{year}-{sequence:D5}";

            // Check uniqueness
            var exists = await _db.Invoices.AnyAsync(i => i.InvoiceNumber == number);
            if (!exists)
                return number;
        }

        // Fallback with GUID suffix for uniqueness
        return $"{prefix}-{year}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
    }

    private static decimal CalculateTieredSeatPrice(Plan plan, int seatCount)
    {
        var tiers = plan.PricingTiers.OrderBy(t => t.MinUnits).ToList();
        if (tiers.Count == 0) return seatCount * (plan.PerSeatMonthlyPrice ?? 0);

        decimal total = 0;
        int remaining = seatCount;

        foreach (var tier in tiers)
        {
            if (remaining <= 0) break;

            var tierMax = tier.MaxUnits ?? int.MaxValue;
            var tierUnits = Math.Min(remaining, tierMax - tier.MinUnits + 1);
            total += tierUnits * tier.PricePerUnit;
            remaining -= tierUnits;
        }

        return total;
    }

    private void SnapshotCompanyDetails(Invoice invoice, Modules.Tenancy.Entities.Tenant tenant)
    {
        invoice.CompanyName = _options.Company.Name;
        invoice.CompanyAddress = _options.Company.Address;
        invoice.CompanyVatNumber = _options.Company.VatNumber;

        if (tenant.BillingProfile is not null)
        {
            invoice.TenantCompanyName = tenant.BillingProfile.CompanyName;
            invoice.TenantBillingAddress = string.Join(", ",
                new[] { tenant.BillingProfile.BillingAddress, tenant.BillingProfile.City, tenant.BillingProfile.Province, tenant.BillingProfile.PostalCode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            invoice.TenantVatNumber = tenant.BillingProfile.VatNumber;
        }
    }
}
