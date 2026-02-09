using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using saas.Data.Core;
using saas.Modules.Marketing.Models;
using saas.Shared;
using Swap.Htmx;

namespace saas.Modules.Marketing.Controllers;

public class MarketingController : SwapController
{
    private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", RegexOptions.Compiled);

    private readonly CoreDbContext _coreDb;
    private readonly IEmailService _emailService;
    private readonly IBotProtection _botProtection;
    private readonly SiteSettings _site;
    private readonly ILogger<MarketingController> _logger;

    public MarketingController(
        CoreDbContext coreDb,
        IEmailService emailService,
        IBotProtection botProtection,
        IOptions<SiteSettings> siteOptions,
        ILogger<MarketingController> logger)
    {
        _coreDb = coreDb;
        _emailService = emailService;
        _botProtection = botProtection;
        _site = siteOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Modern teams run here";
        return SwapView();
    }

    [HttpGet]
    public async Task<IActionResult> Pricing()
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.MonthlyPrice)
            .ToListAsync();

        ViewData["Title"] = "Pricing";
        return SwapView(plans);
    }

    [HttpGet]
    public IActionResult About()
    {
        ViewData["Title"] = "About";
        return SwapView();
    }

    [HttpGet]
    public IActionResult Contact()
    {
        ViewData["Title"] = "Contact";
        return SwapView(new ContactRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("contact")]
    public async Task<IActionResult> Contact([FromForm] ContactRequest request)
    {
        if (!ModelState.IsValid)
        {
            var error = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Please complete all fields.";
            return PartialView("_ContactResult", new { Success = false, Message = error });
        }

        var botCheck = await _botProtection.ValidateAsync(request.CaptchaToken ?? string.Empty);
        if (!botCheck)
        {
            _logger.LogWarning("Bot protection failed for contact submission: {Email}", request.Email);
            return PartialView("_ContactResult", new { Success = false, Message = "Bot protection verification failed." });
        }

        if (string.IsNullOrWhiteSpace(_site.SupportEmail))
        {
            _logger.LogWarning("Support email missing, cannot send contact message.");
            return PartialView("_ContactResult", new { Success = false, Message = "Contact service is unavailable." });
        }

        var safeName = WebUtility.HtmlEncode(request.Name);
        var safeEmail = WebUtility.HtmlEncode(request.Email);
        var safeMessage = WebUtility.HtmlEncode(request.Message).Replace("\n", "<br />");

        var htmlBody = $@"
            <h2>New contact request</h2>
            <p><strong>Name:</strong> {safeName}</p>
            <p><strong>Email:</strong> {safeEmail}</p>
            <p><strong>Message:</strong></p>
            <p>{safeMessage}</p>";

        await _emailService.SendAsync(new EmailMessage(
            _site.SupportEmail,
            $"Contact request from {request.Name}",
            htmlBody,
            $"Name: {request.Name}\nEmail: {request.Email}\n\n{request.Message}"));

        _logger.LogInformation("Contact message sent from {Email}", request.Email);

        return PartialView("_ContactResult", new { Success = true, Message = "Thanks for reaching out. We will reply soon." });
    }

    [HttpGet]
    public IActionResult Terms()
    {
        ViewData["Title"] = "Terms";
        return SwapView();
    }

    [HttpGet]
    public IActionResult Privacy()
    {
        ViewData["Title"] = "Privacy";
        return SwapView();
    }

    [HttpGet]
    public IActionResult LoginRedirect([FromQuery] string? slug)
    {
        var model = new LoginRedirectModel { Slug = slug ?? string.Empty };

        if (!string.IsNullOrWhiteSpace(slug))
        {
            var normalized = slug.Trim().ToLowerInvariant();
            if (!IsValidSlug(normalized))
            {
                model.ErrorMessage = "Enter a valid workspace slug (3-63 chars).";
                return SwapView(model);
            }

            return Redirect($"/{normalized}/login");
        }

        return SwapView(model);
    }

    [HttpGet]
    public IActionResult LoginModal()
    {
        return SwapView("_LoginModal", new LoginRedirectModel());
    }

    [HttpGet]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)]
    public IActionResult Sitemap()
    {
        var baseUrl = _site.BaseUrl.TrimEnd('/');
        var urls = new[]
        {
            "",
            "/pricing",
            "/about",
            "/contact",
            "/legal/terms",
            "/legal/privacy"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        var lastMod = DateTime.UtcNow.ToString("yyyy-MM-dd");
        foreach (var path in urls)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseUrl}{path}</loc>");
            sb.AppendLine($"    <lastmod>{lastMod}</lastmod>");
            sb.AppendLine("  </url>");
        }

        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml");
    }

    [HttpGet]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots()
    {
        var baseUrl = _site.BaseUrl.TrimEnd('/');
        var content = $"User-agent: *\nDisallow: /super-admin/\nSitemap: {baseUrl}/sitemap.xml\n";
        return Content(content, "text/plain");
    }

    private static bool IsValidSlug(string slug)
    {
        return slug.Length is >= 3 and <= 63 && SlugPattern.IsMatch(slug);
    }
}
