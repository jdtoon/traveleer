using MassTransit;
using saas.Shared;
using saas.Shared.Messages;

namespace saas.Infrastructure.Messaging.Consumers;

/// <summary>
/// Sends a welcome email when a new tenant is created.
/// </summary>
public class TenantCreatedConsumer : IConsumer<TenantCreatedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<TenantCreatedConsumer> _logger;

    public TenantCreatedConsumer(IEmailService emailService, ILogger<TenantCreatedConsumer> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TenantCreatedEvent> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Tenant created: {TenantName} ({Slug}) on plan {PlanSlug}",
            msg.TenantName, msg.Slug, msg.PlanSlug);

        await _emailService.SendAsync(new EmailMessage(
            msg.ContactEmail,
            $"Welcome to {msg.TenantName}!",
            $"Your workspace '{msg.TenantName}' has been created. " +
            $"Sign in at /{msg.Slug}/login to get started."));
    }
}

/// <summary>
/// Handles plan change events — logs and sends notification email.
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
        return Task.CompletedTask;
    }
}

/// <summary>
/// Processes email send commands asynchronously via queue.
/// </summary>
public class SendEmailConsumer : IConsumer<SendEmailCommand>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<SendEmailConsumer> _logger;

    public SendEmailConsumer(IEmailService emailService, ILogger<SendEmailConsumer> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendEmailCommand> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Sending email to {To}: {Subject}", msg.ToAddress, msg.Subject);

        // Build body from template data
        var body = msg.TemplateData.Count > 0
            ? string.Join("\n", msg.TemplateData.Select(kv => $"{kv.Key}: {kv.Value}"))
            : $"Template: {msg.TemplateName}";

        await _emailService.SendAsync(new EmailMessage(msg.ToAddress, msg.Subject, body));
    }
}
