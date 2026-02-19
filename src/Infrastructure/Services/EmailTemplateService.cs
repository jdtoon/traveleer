using System.Net;
using Microsoft.Extensions.Options;
using saas.Shared;

namespace saas.Infrastructure.Services;

/// <summary>
/// Simple email template engine using {{Variable}} placeholder replacement.
/// Templates live in the EmailTemplates folder. Layout variables (AppName, Year) are auto-injected.
/// </summary>
public interface IEmailTemplateService
{
    string Render(string templateName, Dictionary<string, string> variables);
}

public class EmailTemplateService : IEmailTemplateService
{
    private readonly string _templateDirectory;
    private readonly string _appName;
    private readonly Dictionary<string, string> _templateCache = new();

    public EmailTemplateService(IWebHostEnvironment env, IOptions<SiteSettings> siteOptions)
    {
        _templateDirectory = Path.Combine(env.ContentRootPath, "EmailTemplates");
        _appName = siteOptions.Value.Name;
    }

    public string Render(string templateName, Dictionary<string, string> variables)
    {
        var template = LoadTemplate(templateName);

        // Apply layout wrapper
        var layout = LoadTemplate("_Layout");
        template = layout.Replace("{{Content}}", template);

        // Auto-inject layout-level variables (system-controlled, safe — no encoding)
        template = template.Replace("{{AppName}}", _appName);
        template = template.Replace("{{Year}}", DateTime.UtcNow.Year.ToString());

        // Replace caller-provided variables with HTML encoding to prevent injection
        foreach (var kvp in variables)
        {
            template = template.Replace($"{{{{{kvp.Key}}}}}", WebUtility.HtmlEncode(kvp.Value));
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
