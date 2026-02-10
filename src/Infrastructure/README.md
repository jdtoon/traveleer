# Infrastructure

Middleware, service registration, provider switching, and cross-cutting concerns.

## Key Files

| File | Purpose |
|------|---------|
| `ServiceCollectionExtensions.cs` | All DI registration: databases, core services, email/billing/storage provider switching, rate limiting |
| `ApplicationBuilderExtensions.cs` | Database initialization, migrations, seeding, middleware pipeline, endpoint mapping |
| `ErrorPages.cs` | Pre-MVC error pages (404/403) read from `wwwroot/errors/` HTML files |
| `ModuleViewLocationExpander.cs` | Routes Razor views to module folders |
| `MvcExtensions.cs` | MVC configuration with module view paths |
| `WebOptimizerExtensions.cs` | CSS/JS bundling and minification |

## Middleware Pipeline (order matters)

1. `ResponseCompression`
2. `ForwardedHeaders`
3. `SecurityHeadersMiddleware` — CSP, HSTS headers
4. `ExceptionHandler` (production) → `/Home/Error`
5. `StaticFiles`
6. `Routing`
7. `RateLimiter`
8. `TenantResolutionMiddleware` — resolves `/{slug}/` to tenant context
9. `Authentication`
10. `SwapHtmx`
11. `Authorization`
12. `CurrentUserMiddleware` — populates `ICurrentUser`

## Provider Switching

All providers are config-driven via `{Section}:Provider`:

| Service | Config Key | Options |
|---------|-----------|---------|
| Email | `Email:Provider` | `Console` (dev), `SES` (production) |
| Billing | `Billing:Provider` | `Mock` (dev), `Paystack` (production) |
| Bot Protection | `Turnstile:Provider` | `Mock` (dev), `Cloudflare` (production) |
| Storage | `Storage:Provider` | `Local` (dev), `R2` (production) |
