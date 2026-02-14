using MassTransit;
using saas.Shared.Messages;

namespace saas.Infrastructure.Messaging.Consumers;

/// <summary>
/// Sends a welcome email when a new tenant is created.
/// </summary>
public class TenantCreatedConsumer : IConsumer<TenantCreatedEvent>
{
    private readonly ILogger<TenantCreatedConsumer> _logger;

    public TenantCreatedConsumer(ILogger<TenantCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TenantCreatedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Tenant created: {TenantName} ({Slug}) on plan {PlanSlug}",
            msg.TenantName, msg.Slug, msg.PlanSlug);

        // TODO: Send welcome email, provision resources, etc.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles plan change events — update feature flags, notify tenant, etc.
/// </summary>
public class TenantPlanChangedConsumer : IConsumer<TenantPlanChangedEvent>
{
    private readonly ILogger<TenantPlanChangedConsumer> _logger;

    public TenantPlanChangedConsumer(ILogger<TenantPlanChangedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<TenantPlanChangedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Tenant {Slug} changed plan from {OldPlan} to {NewPlan}",
            msg.Slug, msg.OldPlanSlug, msg.NewPlanSlug);

        // TODO: Update feature flags, send notification
        return Task.CompletedTask;
    }
}

/// <summary>
/// Processes email send commands asynchronously.
/// </summary>
public class SendEmailConsumer : IConsumer<SendEmailCommand>
{
    private readonly Shared.IEmailService _emailService;
    private readonly ILogger<SendEmailConsumer> _logger;

    public SendEmailConsumer(Shared.IEmailService emailService, ILogger<SendEmailConsumer> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendEmailCommand> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Sending email to {To}: {Subject}", msg.ToAddress, msg.Subject);

        // For now, use the existing email service with a simple body
        // TODO: Integrate with Razor email templates (Item 18)
        var body = $"Template: {msg.TemplateName}";
        await _emailService.SendAsync(new Shared.EmailMessage(msg.ToAddress, msg.Subject, body));
    }
}
