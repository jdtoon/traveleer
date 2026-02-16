using MassTransit;

namespace saas.Infrastructure.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddMessagingConfig(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMassTransit(bus =>
        {
            // Auto-discover consumers in this assembly
            bus.AddConsumers(typeof(Program).Assembly);

            var provider = configuration.GetValue<string>("Messaging:Provider") ?? "InMemory";

            if (provider.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
            {
                bus.UsingRabbitMq((context, cfg) =>
                {
                    var host = configuration.GetValue<string>("Messaging:RabbitMQ:Host") ?? "localhost";
                    var port = configuration.GetValue<ushort>("Messaging:RabbitMQ:Port");
                    var username = configuration.GetValue<string>("Messaging:RabbitMQ:Username") ?? "guest";
                    var password = configuration.GetValue<string>("Messaging:RabbitMQ:Password") ?? "guest";
                    var vhost = configuration.GetValue<string>("Messaging:RabbitMQ:VirtualHost") ?? "/";

                    cfg.Host(host, port == 0 ? (ushort)5672 : port, vhost, h =>
                    {
                        h.Username(username);
                        h.Password(password);
                    });

                    // Retry policy for transient failures
                    cfg.UseMessageRetry(r => r.Intervals(
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15),
                        TimeSpan.FromSeconds(30)
                    ));

                    cfg.ConfigureEndpoints(context);
                });
            }
            else
            {
                // In-memory transport for development/testing
                bus.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        return services;
    }
}
