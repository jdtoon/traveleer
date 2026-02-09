using System.ComponentModel.DataAnnotations;

namespace saas.Modules.Marketing.Models;

public class ContactRequest
{
    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(120)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Message { get; set; } = string.Empty;

    public string? CaptchaToken { get; set; }
}
