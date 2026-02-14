using Microsoft.Extensions.FileProviders;
using saas.Infrastructure.Services;
using Xunit;

namespace saas.Tests.Infrastructure;

public class EmailTemplateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EmailTemplateService _service;

    public EmailTemplateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "email-template-tests-" + Guid.NewGuid().ToString("N"));
        var templateDir = Path.Combine(_tempDir, "EmailTemplates");
        Directory.CreateDirectory(templateDir);

        // Create layout template
        File.WriteAllText(Path.Combine(templateDir, "_Layout.html"),
            "<html><body>{{Content}}</body></html>");

        // Create test template
        File.WriteAllText(Path.Combine(templateDir, "Welcome.html"),
            "<h1>Hello {{Name}}</h1><p>Welcome to {{AppName}}!</p>");

        // Create template with no variables
        File.WriteAllText(Path.Combine(templateDir, "Simple.html"),
            "<p>No variables here.</p>");

        var env = new FakeWebHostEnvironment(_tempDir);
        _service = new EmailTemplateService(env);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Render_ReplacesVariables()
    {
        var result = _service.Render("Welcome", new Dictionary<string, string>
        {
            ["Name"] = "John",
            ["AppName"] = "SaaSKit"
        });

        Assert.Contains("Hello John", result);
        Assert.Contains("Welcome to SaaSKit!", result);
    }

    [Fact]
    public void Render_WrapsInLayout()
    {
        var result = _service.Render("Simple", new Dictionary<string, string>());

        Assert.StartsWith("<html><body>", result);
        Assert.EndsWith("</body></html>", result);
        Assert.Contains("No variables here.", result);
    }

    [Fact]
    public void Render_ThrowsForMissingTemplate()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _service.Render("NonExistent", new Dictionary<string, string>()));
    }

    [Fact]
    public void Render_LeavesUnmatchedPlaceholders()
    {
        var result = _service.Render("Welcome", new Dictionary<string, string>
        {
            ["Name"] = "Test"
            // AppName intentionally not provided
        });

        Assert.Contains("Hello Test", result);
        Assert.Contains("{{AppName}}", result);
    }

    [Fact]
    public void Render_CachesTemplates()
    {
        // First call loads from disk
        var result1 = _service.Render("Simple", new Dictionary<string, string>());

        // Delete the file — second call should still work from cache
        var templateDir = Path.Combine(_tempDir, "EmailTemplates");
        File.Delete(Path.Combine(templateDir, "Simple.html"));

        var result2 = _service.Render("Simple", new Dictionary<string, string>());
        Assert.Equal(result1, result2);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private class FakeWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string contentRoot) => ContentRootPath = contentRoot;
        public string ContentRootPath { get; set; }
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "TestApp";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
