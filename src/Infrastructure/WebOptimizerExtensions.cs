using WebOptimizer;

namespace saas.Infrastructure;

public static class WebOptimizerExtensions
{
    public static IServiceCollection AddWebOptimizerConfig(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddWebOptimizer(pipeline =>
        {
            // JavaScript bundle (layout utilities only — Tailwind browser runtime removed)
            pipeline.AddJavaScriptBundle("/js/bundle.js",
                "js/layout.js"
            );

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
