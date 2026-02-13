using System.ComponentModel.DataAnnotations;

namespace saas.Modules.Registration.Models;

public class RegisterRequest
{
    [Required(ErrorMessage = "Slug is required")]
    [StringLength(63, MinimumLength = 3, ErrorMessage = "Slug must be between 3 and 63 characters")]
    [RegularExpression(@"^[a-z0-9][a-z0-9-]*[a-z0-9]$",
        ErrorMessage = "Slug must contain only lowercase letters, numbers, and hyphens, and must start and end with a letter or number")]
    public string Slug { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Plan is required")]
    public Guid PlanId { get; set; }

    public string BillingCycle { get; set; } = "Monthly";

    public string? CaptchaToken { get; set; }
}
