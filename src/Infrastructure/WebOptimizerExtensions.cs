using WebOptimizer;

namespace saas.Infrastructure;

public static class WebOptimizerExtensions
{
    public static IServiceCollection AddWebOptimizerConfig(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddWebOptimizer(pipeline =>
        {
            // Minify in production
            if (!environment.IsDevelopment())
            {
                pipeline.MinifyCssFiles();
                pipeline.MinifyJsFiles();
            }
        });

        return services;
    }
}
