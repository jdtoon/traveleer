using saas.Data;

namespace saas.Modules.Branding.Entities;

public class BrandingSettings : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SingletonKey { get; set; } = 1;

    public string? AgencyName { get; set; }
    public string? PublicContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }

    public string PrimaryColor { get; set; } = "#2563EB";
    public string SecondaryColor { get; set; } = "#1E3A5F";

    public string QuotePrefix { get; set; } = "QT";
    public string QuoteNumberFormat { get; set; } = "{PREFIX}-{YEAR}-{SEQ:4}";
    public int NextQuoteSequence { get; set; } = 1;
    public bool QuoteResetSequenceYearly { get; set; } = true;
    public int? QuoteSequenceLastResetYear { get; set; }
    public int DefaultQuoteValidityDays { get; set; } = 14;
    public decimal? DefaultQuoteMarkupPercentage { get; set; }
    public string? PdfFooterText { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
