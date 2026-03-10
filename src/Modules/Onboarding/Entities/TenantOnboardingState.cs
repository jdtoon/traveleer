using saas.Data;
using saas.Modules.Onboarding.DTOs;

namespace saas.Modules.Onboarding.Entities;

public class TenantOnboardingState : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CurrentStep { get; set; } = 1;
    public string PreferredWorkspace { get; set; } = OnboardingWorkspaceOptions.Quotes;
    public DateTime? IdentityCompletedAt { get; set; }
    public DateTime? DefaultsCompletedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? SkippedAt { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
