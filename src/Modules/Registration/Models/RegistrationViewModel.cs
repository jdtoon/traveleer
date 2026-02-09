using saas.Data.Core;

namespace saas.Modules.Registration.Models;

public class RegistrationViewModel
{
    public IReadOnlyList<Plan> Plans { get; init; } = Array.Empty<Plan>();
    public Guid? SelectedPlanId { get; init; }
}
