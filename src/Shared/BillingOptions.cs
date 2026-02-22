namespace saas.Shared;

public class BillingOptions
{
    public const string SectionName = "Billing";

    public string Provider { get; set; } = "Mock";
    public string Currency { get; set; } = "ZAR";

    public TaxOptions Tax { get; set; } = new();
    public CompanyOptions Company { get; set; } = new();
    public InvoiceOptions Invoice { get; set; } = new();
    public TrialOptions Trial { get; set; } = new();
    public GracePeriodOptions GracePeriod { get; set; } = new();
    public BillingFeatureToggles Features { get; set; } = new();
    public Dictionary<string, UsageMetricConfig> UsageMetrics { get; set; } = new();
}

public class TaxOptions
{
    public decimal Rate { get; set; } = 0.15m;
    public string Label { get; set; } = "VAT";
    public bool Included { get; set; } = true;
}

public class CompanyOptions
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
}

public class InvoiceOptions
{
    public string Prefix { get; set; } = "INV";
    public int PaymentTermDays { get; set; }
}

public class TrialOptions
{
    public int? DefaultDays { get; set; } = 14;
}

public class GracePeriodOptions
{
    public int Days { get; set; } = 3;
    public int DunningAttempts { get; set; } = 3;
    public int DunningIntervalHours { get; set; } = 72;
}

public class BillingFeatureToggles
{
    public bool AnnualBilling { get; set; } = true;
    public bool PerSeatBilling { get; set; }
    public bool UsageBilling { get; set; }
    public bool AddOns { get; set; }
    public bool Discounts { get; set; } = true;
    public bool SetupFees { get; set; }
}

public class UsageMetricConfig
{
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, long?> IncludedByPlan { get; set; } = new();
    public decimal OveragePrice { get; set; }
}
