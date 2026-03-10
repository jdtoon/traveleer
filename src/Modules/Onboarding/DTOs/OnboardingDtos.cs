using System.ComponentModel.DataAnnotations;

namespace saas.Modules.Onboarding.DTOs;

public static class OnboardingWorkspaceOptions
{
    public const string Quotes = "quotes";
    public const string RateCards = "rate-cards";
    public const string Bookings = "bookings";

    public static readonly IReadOnlyList<string> All = [Quotes, RateCards, Bookings];
}

public class OnboardingPageDto
{
    public string TenantName { get; set; } = string.Empty;
    public int CurrentStep { get; set; } = 1;
    public bool IsCompleted { get; set; }
    public bool CanSkip { get; set; }
    public List<OnboardingStepSummaryDto> Steps { get; set; } = [];
    public OnboardingPreviewDto Preview { get; set; } = new();
    public OnboardingIdentityStepDto IdentityStep { get; set; } = new();
    public OnboardingDefaultsStepDto DefaultsStep { get; set; } = new();
    public OnboardingCompletionStepDto CompletionStep { get; set; } = new();
}

public class OnboardingStepSummaryDto
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public bool IsComplete { get; set; }
    public bool IsAvailable { get; set; }
}

public class OnboardingPreviewDto
{
    public string EffectiveAgencyName { get; set; } = string.Empty;
    public string EffectiveContactEmail { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#2563EB";
    public string SecondaryColor { get; set; } = "#1E3A5F";
    public string PrimaryTextColor { get; set; } = "#FFFFFF";
    public string SecondaryTextColor { get; set; } = "#FFFFFF";
    public string PreviewReferenceNumber { get; set; } = string.Empty;
    public string PreferredWorkspace { get; set; } = OnboardingWorkspaceOptions.Quotes;
    public string PreferredWorkspaceLabel { get; set; } = "Quotes";
    public string NextActionUrl { get; set; } = "/";
    public string NextActionLabel { get; set; } = "Open Quotes";
}

public class OnboardingIdentityStepDto
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

    [Url(ErrorMessage = "Enter a valid logo URL.")]
    [StringLength(500)]
    public string? LogoUrl { get; set; }

    [Required(ErrorMessage = "Primary color is required.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a 6-digit hex color like #2563EB.")]
    public string PrimaryColor { get; set; } = "#2563EB";

    [Required(ErrorMessage = "Secondary color is required.")]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a 6-digit hex color like #1E3A5F.")]
    public string SecondaryColor { get; set; } = "#1E3A5F";
}

public class OnboardingDefaultsStepDto
{
    [Required(ErrorMessage = "Quote prefix is required.")]
    [StringLength(20)]
    public string QuotePrefix { get; set; } = "QT";

    [Range(1, 365, ErrorMessage = "Default validity should be between 1 and 365 days.")]
    public int DefaultQuoteValidityDays { get; set; } = 14;

    [Range(typeof(decimal), "0", "999", ErrorMessage = "Default markup must be zero or more.")]
    public decimal? DefaultQuoteMarkupPercentage { get; set; }

    public bool QuoteResetSequenceYearly { get; set; } = true;

    [Required(ErrorMessage = "Choose where your team wants to start.")]
    [RegularExpression("^(quotes|rate-cards|bookings)$", ErrorMessage = "Choose a valid workspace.")]
    public string PreferredWorkspace { get; set; } = OnboardingWorkspaceOptions.Quotes;
}

public class OnboardingCompletionStepDto
{
    public bool IsCompleted { get; set; }
    public string Headline { get; set; } = "You are ready to start selling.";
    public string Summary { get; set; } = string.Empty;
    public string NextActionUrl { get; set; } = "/";
    public string NextActionLabel { get; set; } = "Open Quotes";
    public List<string> Highlights { get; set; } = [];
}
