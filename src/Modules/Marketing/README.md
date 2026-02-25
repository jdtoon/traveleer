# Marketing Module

Public-facing marketing pages — landing page, pricing, about, contact, legal pages, SEO (sitemap/robots), and the login redirect flow. These pages are served without tenant context and use a dedicated `_MarketingLayout`.

## Structure

```
Marketing/
├── MarketingModule.cs
├── Models/
│   ├── ContactRequest.cs               # Name, Email, Message, TurnstileToken
│   ├── LoginRedirectModel.cs           # Slug field for redirect form
│   └── MarketingPricingViewModel.cs    # Plans list + billing cycle toggle
├── Controllers/
│   └── MarketingController.cs          # SwapController — all public routes
└── Views/
    ├── Marketing/
    │   ├── Index.cshtml                # Landing page (hero, features, CTA)
    │   ├── Pricing.cshtml              # Plan comparison with toggle
    │   ├── About.cshtml
    │   ├── Contact.cshtml              # Contact form with bot protection
    │   ├── LoginRedirect.cshtml        # "Enter your workspace" flow
    │   ├── Privacy.cshtml
    │   ├── Terms.cshtml
    │   ├── _ContactResult.cshtml       # HTMX partial for form result
    │   ├── _LoginModal.cshtml          # HTMX partial for login overlay
    │   └── _PricingContent.cshtml      # HTMX partial for pricing toggle
    └── Shared/
        └── _MarketingLayout.cshtml     # Marketing page layout (different from tenant layout)
```

## Routes

| Method | URL | Action | Notes |
|--------|-----|--------|-------|
| GET | `/` | `Index` | Landing page |
| GET | `/pricing` | `Pricing` | Plan comparison (loads from DB) |
| GET | `/pricing/content` | `PricingContent` | HTMX partial — switches monthly/annual |
| GET | `/about` | `About` | About page |
| GET | `/contact` | `Contact` | Contact form |
| POST | `/contact` | `Contact` | Submit form — rate limited (`contact` policy), bot protected |
| GET | `/legal/terms` | `Terms` | Terms of service |
| GET | `/legal/privacy` | `Privacy` | Privacy policy |
| GET | `/login-redirect` | `LoginRedirect` | "Enter your workspace slug" page |
| GET | `/login-modal` | `LoginModal` | HTMX partial for login slug input |
| GET | `/sitemap.xml` | `Sitemap` | Dynamic XML sitemap (cached 10 min) |
| GET | `/robots.txt` | `Robots` | Standard robots.txt (cached 10 min) |

## Public Route Prefixes

Registered with `TenantResolutionMiddleware` so these paths are never treated as tenant slugs:

```
pricing, about, contact, legal, sitemap.xml, robots.txt
```

## Key Behaviors

### Pricing Page
- Loads all active plans from `CoreDbContext.Plans` and renders them as pricing cards
- Supports `?mode=annual` query parameter to toggle billing cycle display
- Monthly/annual toggle uses HTMX to swap `_PricingContent` partial without full page reload
- Each plan card links to `/register?plan={planId}`

### Contact Form
- Rate limited: `contact` policy (default 3 submissions per 5 minutes)
- Bot protected via `IBotProtection` (Turnstile in production, Mock in development)
- Sends email via `IEmailService` to the configured `Site:SupportEmail`
- Returns `_ContactResult` partial via HTMX swap

### Login Redirect
- User enters their workspace slug → redirected to `/{slug}/auth/login`
- Used when users arrive at the marketing site and need to find their workspace

### SEO
- `Sitemap()` generates an XML sitemap with all marketing page URLs
- `Robots()` serves a standard `robots.txt` referencing the sitemap
- Both are cached for 10 minutes via `[ResponseCache]`

## Customization

### Changing Page Content
Edit the `.cshtml` views directly. The layout is `_MarketingLayout.cshtml` — separate from the tenant app layout.

### Adding New Pages
1. Add a new action to `MarketingController`
2. Create the view in `Views/Marketing/`
3. If the new page needs its own URL prefix (e.g. `/blog`), add it to `PublicRoutePrefixes` in `MarketingModule.cs`

### Pricing Plans
Plans are loaded live from the database. To change pricing, update plans via the SuperAdmin dashboard or `CoreDataSeeder`. No hardcoded prices in view templates.
