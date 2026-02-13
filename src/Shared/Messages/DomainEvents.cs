namespace saas.Shared.Messages;

/// <summary>
/// Published when a new tenant completes registration and their database is provisioned.
/// </summary>
public record TenantCreatedEvent(
    int TenantId,
    string TenantName,
    string Slug,
    string ContactEmail,
    string PlanSlug,
    DateTime CreatedAtUtc
);

/// <summary>
/// Published when a tenant's subscription plan changes (upgrade or downgrade).
/// </summary>
public record TenantPlanChangedEvent(
    int TenantId,
    string Slug,
    string OldPlanSlug,
    string NewPlanSlug,
    DateTime ChangedAtUtc
);

/// <summary>
/// Published when a tenant is suspended (e.g., non-payment, admin action).
/// </summary>
public record TenantSuspendedEvent(
    int TenantId,
    string Slug,
    string Reason,
    DateTime SuspendedAtUtc
);

/// <summary>
/// Published when a user is invited to a tenant workspace.
/// </summary>
public record UserInvitedEvent(
    int TenantId,
    string Slug,
    string InvitedEmail,
    string InvitedByEmail,
    string Role,
    DateTime InvitedAtUtc
);

/// <summary>
/// Published when a user successfully logs in.
/// </summary>
public record UserLoggedInEvent(
    string UserId,
    string Email,
    int TenantId,
    string Slug,
    DateTime LoggedInAtUtc
);

/// <summary>
/// Command to send a transactional email.
/// </summary>
public record SendEmailCommand(
    string ToAddress,
    string ToName,
    string Subject,
    string TemplateName,
    Dictionary<string, string> TemplateData
);

/// <summary>
/// Published when a billing subscription event occurs (payment succeeded, failed, etc.).
/// </summary>
public record SubscriptionPaymentEvent(
    int TenantId,
    string Slug,
    string EventType,
    decimal Amount,
    string Currency,
    string GatewayReference,
    DateTime OccurredAtUtc
);
