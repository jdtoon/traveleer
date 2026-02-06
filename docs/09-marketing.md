# 09 — Marketing Module

> Defines the public-facing marketing website that visitors see before signing up. Covers the landing page, pricing page (driven by plans from `CoreDbContext`), the registration entry point, legal pages, and SEO considerations.

**Prerequisites**: [01 — Architecture](01-architecture.md), [03 — Modules](03-modules.md), [06 — Billing & Paystack](06-billing-paystack.md)

---

## 1. Overview

The Marketing module handles every page a **visitor** sees before they belong to a tenant. These pages are public (no authentication), live at the root URL (no `/{slug}/` prefix), and exist to convert visitors into registered tenants.

```
https://myapp.com/                  ← Landing page
https://myapp.com/pricing           ← Pricing page (reads plans from DB)
https://myapp.com/register          ← Registration form (→ Registration module)
https://myapp.com/about             ← About page
https://myapp.com/contact           ← Contact page
https://myapp.com/legal/terms       ← Terms of service
https://myapp.com/legal/privacy     ← Privacy policy
```

### Marketing vs. Tenant Routes

| Route Pattern | Module | Auth Required | Layout |
|---------------|--------|---------------|--------|
| `/` `/pricing` `/about` `/contact` `/legal/*` | Marketing | No | Marketing layout (public) |
| `/register` `/register/*` | Registration | No | Marketing layout (public) |
| `/super-admin/*` | SuperAdmin | Super admin cookie | Admin layout |
| `/{slug}/*` | Tenant modules | Tenant cookie | Tenant app layout |

The Marketing module uses a **different layout** from the tenant application — no sidebar, no drawer, no app chrome. Just a clean public website.

---

## 2. Marketing Layout

A separate layout for public-facing pages, distinct from the authenticated app layout (`_Layout.cshtml`):

```
Views/
├── Shared/
│   ├── _Layout.cshtml              ← Tenant app layout (sidebar, drawer)
│   └── _MarketingLayout.cshtml     ← Public marketing layout (navbar, footer)
```

### _MarketingLayout.cshtml

```html
<!DOCTYPE html>
<html lang="en" data-theme="corporate">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - MyApp</title>
    <meta name="description" content="@ViewData["Description"]" />

    <!-- Open Graph -->
    <meta property="og:title" content="@ViewData["Title"] - MyApp" />
    <meta property="og:description" content="@ViewData["Description"]" />
    <meta property="og:type" content="website" />
    <meta property="og:url" content="@Context.Request.GetDisplayUrl()" />

    <!-- DaisyUI + Tailwind CSS -->
    <link rel="stylesheet" href="/css/styles.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/_content/Swap.Htmx/css/swap.css" asp-append-version="true" />

    @await RenderSectionAsync("Head", required: false)
</head>
<body class="min-h-screen bg-base-100 text-base-content flex flex-col">

    <!-- Navbar -->
    <header class="navbar bg-base-100 border-b border-base-300 px-4 lg:px-8">
        <div class="flex-1">
            <a href="/" class="text-xl font-bold">MyApp</a>
        </div>
        <div class="flex-none">
            <ul class="menu menu-horizontal gap-1">
                <li><a href="/pricing">Pricing</a></li>
                <li><a href="/about">About</a></li>
                <li><a href="/contact">Contact</a></li>
            </ul>
            <div class="ml-4 flex gap-2">
                <a href="/register" class="btn btn-primary btn-sm">Get Started</a>
                <a href="#" id="tenant-login-btn" class="btn btn-ghost btn-sm">Sign In</a>
            </div>
        </div>

        <!-- Mobile menu -->
        <div class="flex-none lg:hidden">
            <div class="dropdown dropdown-end">
                <label tabindex="0" class="btn btn-ghost btn-square">
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none"
                         viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                              d="M4 6h16M4 12h16M4 18h16" />
                    </svg>
                </label>
                <ul tabindex="0"
                    class="menu menu-sm dropdown-content mt-3 z-[1] p-2 shadow bg-base-100 rounded-box w-52">
                    <li><a href="/pricing">Pricing</a></li>
                    <li><a href="/about">About</a></li>
                    <li><a href="/contact">Contact</a></li>
                    <li class="mt-2"><a href="/register" class="btn btn-primary btn-sm">Get Started</a></li>
                    <li><a href="#" class="btn btn-ghost btn-sm">Sign In</a></li>
                </ul>
            </div>
        </div>
    </header>

    <!-- Main Content -->
    <main class="flex-1" id="main-content">
        @RenderBody()
    </main>

    <!-- Footer -->
    <footer class="footer footer-center bg-base-200 text-base-content p-10">
        <nav class="grid grid-flow-col gap-4">
            <a href="/about" class="link link-hover">About</a>
            <a href="/contact" class="link link-hover">Contact</a>
            <a href="/legal/terms" class="link link-hover">Terms</a>
            <a href="/legal/privacy" class="link link-hover">Privacy</a>
        </nav>
        <aside>
            <p>© @DateTime.UtcNow.Year MyApp. All rights reserved.</p>
        </aside>
    </footer>

    <!-- Toast container -->
    <div id="toast-container" class="toast toast-end"></div>
    <div id="modal-container"></div>

    <!-- Scripts -->
    <script src="~/lib/htmx/dist/htmx.min.js" asp-append-version="true"></script>
    <script src="~/_content/Swap.Htmx/js/swap.js" asp-append-version="true"></script>

    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

Marketing views opt into this layout:

```cshtml
@{
    Layout = "~/Views/Shared/_MarketingLayout.cshtml";
}
```

---

## 3. MarketingController

```csharp
public class MarketingController : SwapController
{
    private readonly CoreDbContext _coreDb;

    public MarketingController(CoreDbContext coreDb)
    {
        _coreDb = coreDb;
    }

    // GET /
    [Route("/")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Your SaaS Tagline Here";
        ViewData["Description"] = "A short marketing description for SEO and social sharing.";
        return SwapView();
    }

    // GET /pricing
    [Route("/pricing")]
    public async Task<IActionResult> Pricing()
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        ViewData["Title"] = "Pricing";
        ViewData["Description"] = "Simple, transparent pricing. Start free, upgrade when you're ready.";
        return SwapView(plans);
    }

    // GET /about
    [Route("/about")]
    public IActionResult About()
    {
        ViewData["Title"] = "About";
        ViewData["Description"] = "Learn about our mission and team.";
        return SwapView();
    }

    // GET /contact
    [Route("/contact")]
    public IActionResult Contact()
    {
        ViewData["Title"] = "Contact Us";
        ViewData["Description"] = "Get in touch with our team.";
        return SwapView();
    }

    // GET /legal/terms
    [Route("/legal/terms")]
    public IActionResult Terms()
    {
        ViewData["Title"] = "Terms of Service";
        return SwapView();
    }

    // GET /legal/privacy
    [Route("/legal/privacy")]
    public IActionResult Privacy()
    {
        ViewData["Title"] = "Privacy Policy";
        return SwapView();
    }
}
```

---

## 4. Landing Page — `Index.cshtml`

The landing page is the first impression. It follows a standard SaaS structure:

```
┌──────────────────────────────────────────────────────────────┐
│  [Logo]              Pricing  About  Contact  [Get Started]  │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│                     HERO SECTION                              │
│                                                               │
│          Your SaaS Tagline Goes Here                          │
│          A longer subtitle explaining the value               │
│                                                               │
│          [Get Started Free]    [See Pricing]                  │
│                                                               │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│                   FEATURES SECTION                            │
│                                                               │
│   ┌──────────┐   ┌──────────┐   ┌──────────┐               │
│   │  Feature  │   │  Feature  │   │  Feature  │               │
│   │  Card 1   │   │  Card 2   │   │  Card 3   │               │
│   └──────────┘   └──────────┘   └──────────┘               │
│                                                               │
│   ┌──────────┐   ┌──────────┐   ┌──────────┐               │
│   │  Feature  │   │  Feature  │   │  Feature  │               │
│   │  Card 4   │   │  Card 5   │   │  Card 6   │               │
│   └──────────┘   └──────────┘   └──────────┘               │
│                                                               │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│                 PRICING PREVIEW SECTION                       │
│               (Embedded from /pricing)                        │
│                                                               │
│          [View Full Pricing →]                                │
│                                                               │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│                      CTA SECTION                             │
│                                                               │
│           Ready to get started?                               │
│           [Start Your Free Trial]                             │
│                                                               │
├──────────────────────────────────────────────────────────────┤
│                        FOOTER                                │
│            About · Contact · Terms · Privacy                  │
│                 © 2026 MyApp                                  │
└──────────────────────────────────────────────────────────────┘
```

### Index.cshtml

```html
@{
    Layout = "~/Views/Shared/_MarketingLayout.cshtml";
    ViewData["Title"] = "Your SaaS Tagline Here";
    ViewData["Description"] = "A short marketing description for SEO and social sharing.";
}

<!-- Hero Section -->
<section class="hero min-h-[70vh] bg-base-200">
    <div class="hero-content text-center">
        <div class="max-w-2xl">
            <h1 class="text-5xl font-bold">Your SaaS Tagline Here</h1>
            <p class="py-6 text-lg opacity-70">
                A longer subtitle explaining the core value proposition.
                What problem does this solve? Why should someone care?
            </p>
            <div class="flex justify-center gap-4">
                <a href="/register" class="btn btn-primary btn-lg">Get Started Free</a>
                <a href="/pricing" class="btn btn-ghost btn-lg">See Pricing →</a>
            </div>
        </div>
    </div>
</section>

<!-- Features Section -->
<section class="py-20 px-4 lg:px-8">
    <div class="max-w-6xl mx-auto">
        <h2 class="text-3xl font-bold text-center mb-12">Everything you need</h2>
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-8">

            <div class="card bg-base-100 shadow">
                <div class="card-body">
                    <div class="text-3xl mb-2">🚀</div>
                    <h3 class="card-title">Feature One</h3>
                    <p class="opacity-70">Short description of why this feature matters to the customer.</p>
                </div>
            </div>

            <div class="card bg-base-100 shadow">
                <div class="card-body">
                    <div class="text-3xl mb-2">🔒</div>
                    <h3 class="card-title">Feature Two</h3>
                    <p class="opacity-70">Short description of why this feature matters to the customer.</p>
                </div>
            </div>

            <div class="card bg-base-100 shadow">
                <div class="card-body">
                    <div class="text-3xl mb-2">📊</div>
                    <h3 class="card-title">Feature Three</h3>
                    <p class="opacity-70">Short description of why this feature matters to the customer.</p>
                </div>
            </div>

            <!-- Add more feature cards as needed -->
        </div>
    </div>
</section>

<!-- CTA Section -->
<section class="py-20 bg-primary text-primary-content">
    <div class="text-center max-w-2xl mx-auto px-4">
        <h2 class="text-3xl font-bold mb-4">Ready to get started?</h2>
        <p class="text-lg opacity-80 mb-8">
            Start with the free plan. No credit card required.
        </p>
        <a href="/register" class="btn btn-secondary btn-lg">Start Your Free Trial</a>
    </div>
</section>
```

---

## 5. Pricing Page — `Pricing.cshtml`

The pricing page reads plans from `CoreDbContext` so it's always in sync with the billing system. No hardcoded prices.

```html
@model List<Plan>
@{
    Layout = "~/Views/Shared/_MarketingLayout.cshtml";
    ViewData["Title"] = "Pricing";
    ViewData["Description"] = "Simple, transparent pricing. Start free, upgrade when you're ready.";
}

<section class="py-20 px-4 lg:px-8">
    <div class="max-w-6xl mx-auto">
        <div class="text-center mb-12">
            <h1 class="text-4xl font-bold mb-4">Simple, transparent pricing</h1>
            <p class="text-lg opacity-70">Start free. Upgrade when you're ready. No surprises.</p>

            <!-- Billing cycle toggle -->
            <div class="flex justify-center items-center gap-4 mt-8">
                <span id="monthly-label" class="font-medium">Monthly</span>
                <input type="checkbox" id="billing-toggle" class="toggle toggle-primary"
                       onchange="toggleBillingCycle()" />
                <span id="annual-label" class="opacity-60">
                    Annual <span class="badge badge-success badge-sm">Save 20%</span>
                </span>
            </div>
        </div>

        <!-- Plan Cards -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-@Model.Count gap-8 justify-center">
            @foreach (var plan in Model)
            {
                var isPopular = plan.Name == "Professional"; // Or use a flag on the Plan entity
                <div class="card bg-base-100 shadow-xl @(isPopular ? "border-2 border-primary" : "")">
                    @if (isPopular)
                    {
                        <div class="absolute -top-3 left-1/2 -translate-x-1/2">
                            <span class="badge badge-primary">Most Popular</span>
                        </div>
                    }
                    <div class="card-body text-center">
                        <h2 class="text-2xl font-bold">@plan.Name</h2>
                        <p class="opacity-60 text-sm">@plan.Description</p>

                        <!-- Monthly price (shown by default) -->
                        <div class="my-6 monthly-price">
                            @if (plan.MonthlyPrice == 0)
                            {
                                <span class="text-4xl font-bold">Free</span>
                            }
                            else
                            {
                                <span class="text-4xl font-bold">
                                    R@plan.MonthlyPrice.ToString("N0")
                                </span>
                                <span class="opacity-60">/month</span>
                            }
                        </div>

                        <!-- Annual price (hidden by default) -->
                        <div class="my-6 annual-price hidden">
                            @if (plan.MonthlyPrice == 0)
                            {
                                <span class="text-4xl font-bold">Free</span>
                            }
                            else if (plan.AnnualPrice.HasValue)
                            {
                                <span class="text-4xl font-bold">
                                    R@plan.AnnualPrice.Value.ToString("N0")
                                </span>
                                <span class="opacity-60">/year</span>
                            }
                        </div>

                        <!-- Feature list -->
                        <ul class="text-left space-y-2 my-4">
                            @if (plan.MaxUsers.HasValue)
                            {
                                <li class="flex items-center gap-2">
                                    <span class="text-success">✓</span>
                                    Up to @plan.MaxUsers users
                                </li>
                            }
                            else
                            {
                                <li class="flex items-center gap-2">
                                    <span class="text-success">✓</span>
                                    Unlimited users
                                </li>
                            }
                            <!-- Add more feature bullets from PlanFeatures -->
                        </ul>

                        <div class="card-actions justify-center mt-4">
                            <a href="/register?plan=@plan.Id"
                               class="btn @(isPopular ? "btn-primary" : "btn-outline") w-full">
                                @(plan.MonthlyPrice == 0 ? "Start Free" : "Get Started")
                            </a>
                        </div>
                    </div>
                </div>
            }
        </div>
    </div>
</section>

<!-- FAQ Section -->
<section class="py-20 px-4 lg:px-8 bg-base-200">
    <div class="max-w-3xl mx-auto">
        <h2 class="text-3xl font-bold text-center mb-12">Frequently Asked Questions</h2>

        <div class="space-y-2">
            <div class="collapse collapse-plus bg-base-100">
                <input type="radio" name="faq-accordion" checked="checked" />
                <div class="collapse-title text-lg font-medium">
                    Can I switch plans later?
                </div>
                <div class="collapse-content">
                    <p>Yes! You can upgrade or downgrade at any time. Changes take effect on
                    your next billing cycle.</p>
                </div>
            </div>

            <div class="collapse collapse-plus bg-base-100">
                <input type="radio" name="faq-accordion" />
                <div class="collapse-title text-lg font-medium">
                    Is there a free trial?
                </div>
                <div class="collapse-content">
                    <p>The Free plan is available indefinitely with core features.
                    Paid plans can be tested by upgrading — downgrade anytime.</p>
                </div>
            </div>

            <div class="collapse collapse-plus bg-base-100">
                <input type="radio" name="faq-accordion" />
                <div class="collapse-title text-lg font-medium">
                    What payment methods do you accept?
                </div>
                <div class="collapse-content">
                    <p>We accept all major credit and debit cards through Paystack,
                    including Visa, Mastercard, and Verve.</p>
                </div>
            </div>

            <div class="collapse collapse-plus bg-base-100">
                <input type="radio" name="faq-accordion" />
                <div class="collapse-title text-lg font-medium">
                    Can I cancel anytime?
                </div>
                <div class="collapse-content">
                    <p>Absolutely. Cancel from your billing dashboard at any time.
                    Your account remains active until the end of the current billing period.</p>
                </div>
            </div>
        </div>
    </div>
</section>

@section Scripts {
<script>
    function toggleBillingCycle() {
        const isAnnual = document.getElementById('billing-toggle').checked;
        document.querySelectorAll('.monthly-price').forEach(el =>
            el.classList.toggle('hidden', isAnnual));
        document.querySelectorAll('.annual-price').forEach(el =>
            el.classList.toggle('hidden', !isAnnual));
        document.getElementById('monthly-label').classList.toggle('opacity-60', isAnnual);
        document.getElementById('annual-label').classList.toggle('opacity-60', !isAnnual);
        document.getElementById('monthly-label').classList.toggle('font-medium', !isAnnual);
        document.getElementById('annual-label').classList.toggle('font-medium', isAnnual);
    }
</script>
}
```

---

## 6. Sign In — Tenant Picker

The "Sign In" button in the marketing nav doesn't go to a single login page — the user must specify which tenant they're signing into. This is handled with a small modal or dropdown:

```html
<!-- Sign In Modal (triggered by "Sign In" button) -->
<dialog id="signin-modal" class="modal">
    <div class="modal-box max-w-sm">
        <h3 class="text-lg font-bold mb-4">Sign in to your organisation</h3>
        <form action="/login-redirect" method="GET">
            <div class="form-control">
                <label class="label">
                    <span class="label-text">Organisation URL</span>
                </label>
                <div class="join w-full">
                    <input type="text" name="slug" placeholder="your-org"
                           class="input input-bordered join-item flex-1"
                           pattern="[a-z0-9][a-z0-9-]*[a-z0-9]"
                           required />
                    <span class="join-item flex items-center px-3 bg-base-200 border border-base-300 text-sm opacity-60">
                        .myapp.com
                    </span>
                </div>
            </div>
            <div class="modal-action">
                <button type="submit" class="btn btn-primary w-full">Continue</button>
            </div>
        </form>
        <form method="dialog">
            <button class="btn btn-sm btn-circle btn-ghost absolute right-2 top-2">✕</button>
        </form>
    </div>
    <form method="dialog" class="modal-backdrop"><button>close</button></form>
</dialog>

<script>
    document.getElementById('tenant-login-btn')?.addEventListener('click', (e) => {
        e.preventDefault();
        document.getElementById('signin-modal').showModal();
    });
</script>
```

The `/login-redirect` endpoint simply redirects to `/{slug}/login`:

```csharp
// In MarketingController
[Route("/login-redirect")]
public IActionResult LoginRedirect(string slug)
{
    if (string.IsNullOrWhiteSpace(slug))
        return Redirect("/");

    slug = slug.ToLowerInvariant().Trim();
    return Redirect($"/{slug}/login");
}
```

---

## 7. Static Pages

### About, Contact, Terms, Privacy

These are simple content pages. They can start as static Razor views and optionally be moved to a CMS later.

```
Modules/Marketing/Views/
├── Index.cshtml            ← Landing page
├── Pricing.cshtml          ← Pricing (dynamic from DB)
├── About.cshtml            ← Static content
├── Contact.cshtml          ← Contact form (optional)
├── Terms.cshtml            ← Terms of service (static)
├── Privacy.cshtml          ← Privacy policy (static)
├── LoginRedirect.cshtml    ← Not needed (controller redirects)
└── _ViewImports.cshtml
```

For the contact page, a simple form that sends an email:

```csharp
// POST /contact
[HttpPost("/contact")]
[EnableRateLimiting("strict")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ContactSubmit(ContactRequest request)
{
    if (!await _botProtection.ValidateAsync(request.TurnstileToken))
        return SwapView("Contact").WithToast("Verification failed.", ToastType.Error);

    if (!ModelState.IsValid)
        return SwapView("Contact", request);

    await _email.SendAsync(new EmailMessage
    {
        ToEmail = _configuration["Email:SupportAddress"] ?? "support@localhost",
        Subject = $"Contact Form: {request.Subject}",
        HtmlBody = $"<p><strong>From:</strong> {request.Name} ({request.Email})</p><p>{request.Message}</p>"
    });

    return SwapView("Contact").WithToast("Message sent! We'll get back to you soon.", ToastType.Success);
}

public class ContactRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public string? TurnstileToken { get; set; }
}
```

---

## 8. SEO

### Meta Tags

Every marketing page sets `ViewData["Title"]` and `ViewData["Description"]`, which the layout renders as:

```html
<title>@ViewData["Title"] - MyApp</title>
<meta name="description" content="@ViewData["Description"]" />
<meta property="og:title" content="@ViewData["Title"] - MyApp" />
<meta property="og:description" content="@ViewData["Description"]" />
```

### Sitemap

A dynamic sitemap generated from known marketing routes:

```csharp
// In MarketingController
[Route("/sitemap.xml")]
[ResponseCache(Duration = 3600)]
public IActionResult Sitemap()
{
    var baseUrl = $"{Request.Scheme}://{Request.Host}";

    var urls = new[]
    {
        new { Loc = "/", Priority = "1.0", ChangeFreq = "weekly" },
        new { Loc = "/pricing", Priority = "0.9", ChangeFreq = "weekly" },
        new { Loc = "/about", Priority = "0.5", ChangeFreq = "monthly" },
        new { Loc = "/contact", Priority = "0.5", ChangeFreq = "monthly" },
        new { Loc = "/legal/terms", Priority = "0.3", ChangeFreq = "yearly" },
        new { Loc = "/legal/privacy", Priority = "0.3", ChangeFreq = "yearly" },
    };

    var xml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
        {string.Join("\n", urls.Select(u => $"""
            <url>
                <loc>{baseUrl}{u.Loc}</loc>
                <changefreq>{u.ChangeFreq}</changefreq>
                <priority>{u.Priority}</priority>
            </url>
        """))}
        </urlset>
        """;

    return Content(xml, "application/xml");
}
```

### robots.txt

```csharp
// In MarketingController
[Route("/robots.txt")]
[ResponseCache(Duration = 86400)]
public IActionResult Robots()
{
    var baseUrl = $"{Request.Scheme}://{Request.Host}";
    var content = $"""
        User-agent: *
        Allow: /
        Disallow: /super-admin/
        Disallow: /api/
        Sitemap: {baseUrl}/sitemap.xml
        """;

    return Content(content, "text/plain");
}
```

### Tenant Routes Are Not Indexed

The `robots.txt` doesn't need to explicitly block `/{slug}/` routes because search engines only crawl what's linked. The marketing pages never link to tenant URLs. If you want extra safety:

```
Disallow: /*/login
Disallow: /*/billing
```

---

## 9. Routing Integration

Marketing routes must coexist with tenant `/{slug}/` routes. The routing rules from [01 — Architecture](01-architecture.md) §5 apply:

1. **Exact marketing routes** (`/`, `/pricing`, `/about`, etc.) take priority
2. **Reserved slugs** ([06 — Billing](06-billing-paystack.md) §6) prevent tenants from claiming marketing paths
3. **Catch-all** `/{slug}/{**path}` matches everything else as tenant routes

The `ReservedSlugs` list includes all marketing route segments:

```csharp
// These are already in ReservedSlugs (from 06-billing-paystack.md)
"about", "contact", "legal", "pricing", "register", "login-redirect",
"sitemap.xml", "robots.txt", "health"
```

### Route Registration Order in Program.cs

```csharp
// Marketing routes (explicit, take priority)
app.MapControllerRoute(
    name: "marketing",
    pattern: "{action=Index}",
    defaults: new { controller = "Marketing" },
    constraints: new { action = "Index|Pricing|About|Contact" });

app.MapControllerRoute(
    name: "legal",
    pattern: "legal/{action}",
    defaults: new { controller = "Marketing" });

// Registration routes
app.MapControllerRoute(
    name: "register",
    pattern: "register/{action=Index}",
    defaults: new { controller = "Registration" });

// Super admin routes
app.MapControllerRoute(
    name: "superadmin",
    pattern: "super-admin/{action=Index}",
    defaults: new { controller = "SuperAdmin" });

// Tenant routes (catch-all, last priority)
app.MapControllerRoute(
    name: "tenant",
    pattern: "{slug}/{controller=Home}/{action=Index}/{id?}");
```

---

## 10. Marketing Module Files Summary

```
Modules/Marketing/
├── README.md
├── MarketingModule.cs
├── Controllers/
│   └── MarketingController.cs       # All public pages + sitemap + robots.txt
├── DTOs/
│   └── ContactRequest.cs            # Contact form model
├── Views/
│   ├── _ViewImports.cshtml
│   ├── Index.cshtml                  # Landing page (hero, features, CTA)
│   ├── Pricing.cshtml                # Plan cards (from DB), billing toggle, FAQ
│   ├── About.cshtml                  # Static about page
│   ├── Contact.cshtml                # Contact form with Turnstile
│   ├── Terms.cshtml                  # Terms of service (static)
│   └── Privacy.cshtml                # Privacy policy (static)
└── Events/
    ├── MarketingEventConfig.cs
    └── MarketingEvents.cs

Views/Shared/
└── _MarketingLayout.cshtml           # Public site layout (navbar + footer)
```

---

## 11. Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Separate layout** | Marketing pages need a public-website feel — no sidebar, no app chrome. Clean and focused on conversion. |
| **Pricing from DB** | Prices are defined in `CoreDbContext.Plans`, edited by super admin. The pricing page always reflects the current plans. No hardcoded prices. |
| **No JavaScript framework** | Marketing pages use DaisyUI components + minimal vanilla JS. The billing toggle and FAQ accordion work without a framework. |
| **Tenant login via modal** | A simple slug input modal avoids a dedicated login page that wouldn't know which tenant to authenticate against. |
| **Rate-limited contact form** | The `strict` rate limiter (5/min) + Turnstile prevents spam submissions. |
| **Sitemap & robots.txt as controller actions** | Dynamic generation ensures they stay in sync with routes. Cached aggressively. |

---

## Document Index

This is the final document in the series. The complete documentation suite:

| # | Document | Focus |
|---|----------|-------|
| — | [README](README.md) | Index & technology stack |
| 01 | [Architecture](01-architecture.md) | System overview & project layout |
| 02 | [Database & Multi-Tenancy](02-database-multitenancy.md) | DbContexts, schemas, tenant resolution |
| 03 | [Modules](03-modules.md) | Module contract & inventory |
| 04 | [Authentication & Authorization](04-auth.md) | Magic link, RBAC, permissions |
| 05 | [Feature Flags](05-feature-flags.md) | Database-backed feature management |
| 06 | [Billing & Paystack](06-billing-paystack.md) | Subscriptions, payments, webhooks |
| 07 | [Infrastructure & DevOps](07-infrastructure.md) | Docker, Litestream, email, security |
| 08 | [Local Development](08-local-development.md) | Clone-and-run guide |
| 09 | [Marketing](09-marketing.md) | Public site, pricing, SEO ← **You are here** |
