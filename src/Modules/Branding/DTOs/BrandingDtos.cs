using System.ComponentModel.DataAnnotations;

namespace saas.Modules.Branding.DTOs;

public class BrandingSettingsDto
{
    [StringLength(200)]
    public string? AgencyName { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid public contact email.")]
    [StringLength(320)]
    public string? PublicContactEmail { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    [Url(ErrorMessage = "Enter a valid website URL.")]
    [StringLength(300)]
    public string? Website { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [Url(ErrorMessage = "Enter a valid logo URL.")]
    [StringLength(500)]
    public string? LogoUrl { get; set; }

    [Required(ErrorMessage = "Primary color is required.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a 6-digit hex color like #2563EB.")]
    public string PrimaryColor { get; set; } = "#2563EB";

    [Required(ErrorMessage = "Secondary color is required.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a 6-digit hex color like #1E3A5F.")]
    public string SecondaryColor { get; set; } = "#1E3A5F";

    [Required(ErrorMessage = "Quote prefix is required.")]
    [StringLength(20)]
    public string QuotePrefix { get; set; } = "QT";

    [Required(ErrorMessage = "Quote number format is required.")]
    [StringLength(100)]
    public string QuoteNumberFormat { get; set; } = "{PREFIX}-{YEAR}-{SEQ:4}";

    [Range(1, 999999, ErrorMessage = "Next sequence must be at least 1.")]
    public int NextQuoteSequence { get; set; } = 1;

    public bool QuoteResetSequenceYearly { get; set; } = true;

    [Range(1, 365, ErrorMessage = "Default validity should be between 1 and 365 days.")]
    public int DefaultQuoteValidityDays { get; set; } = 14;

    [Range(typeof(decimal), "0", "999", ErrorMessage = "Default markup must be zero or more.")]
    public decimal? DefaultQuoteMarkupPercentage { get; set; }

    [StringLength(1000)]
    public string? PdfFooterText { get; set; }

    public string PreviewReferenceNumber { get; set; } = string.Empty;
    public string EffectiveAgencyName { get; set; } = string.Empty;
    public string EffectiveContactEmail { get; set; } = string.Empty;
    public string PrimaryTextColor { get; set; } = "#FFFFFF";
    public string SecondaryTextColor { get; set; } = "#FFFFFF";
}

public class BrandingShellDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string AccentColor { get; set; } = "#2563EB";
}

public class BrandingThemeDto
{
    public string PrimaryColor { get; set; } = "#2563EB";
    public string SecondaryColor { get; set; } = "#1E3A5F";
    public string PrimaryTextColor { get; set; } = "#FFFFFF";
    public string SecondaryTextColor { get; set; } = "#FFFFFF";
    public string PrimarySoftColor { get; set; } = "#DBEAFE";
    public string SecondarySoftColor { get; set; } = "#DBEAFE";
    public string ThemeColor { get; set; } = "#2563EB";
}

public class QuoteBrandingDto
{
    public string AgencyName { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    public string? FooterText { get; set; }
}
