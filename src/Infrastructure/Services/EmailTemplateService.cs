namespace saas.Infrastructure.Services;

/// <summary>
/// Razor-inspired email template engine using simple string replacement.
/// Templates live in the EmailTemplates folder. Uses {{Variable}} placeholders.
/// </summary>
public interface IEmailTemplateService
{
    string Render(string templateName, Dictionary<string, string> variables);
}

public class EmailTemplateService : IEmailTemplateService
{
    private readonly string _templateDirectory;
    private readonly Dictionary<string, string> _templateCache = new();

    public EmailTemplateService(IWebHostEnvironment env)
    {
        _templateDirectory = Path.Combine(env.ContentRootPath, "EmailTemplates");
    }

    public string Render(string templateName, Dictionary<string, string> variables)
    {
        var template = LoadTemplate(templateName);

        // Apply layout wrapper
        var layout = LoadTemplate("_Layout");
        template = layout.Replace("{{Content}}", template);

        // Replace variables
        foreach (var kvp in variables)
        {
            template = template.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
        }

        return template;
    }

    private string LoadTemplate(string name)
    {
        if (_templateCache.TryGetValue(name, out var cached))
            return cached;

        var filePath = Path.Combine(_templateDirectory, $"{name}.html");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Email template '{name}' not found at {filePath}");

        var content = File.ReadAllText(filePath);
        _templateCache[name] = content;
        return content;
    }
}
