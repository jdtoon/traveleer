# 07 — Infrastructure & DevOps

> Defines the production deployment model, Docker Compose configuration, Litestream SQLite replication, Cloudflare Turnstile bot protection, ASP.NET Core rate limiting, AWS SES email abstraction, data protection key persistence, and response compression.

**Prerequisites**: [01 — Architecture](01-architecture.md), [02 — Database & Multi-Tenancy](02-database-multitenancy.md)

---

## 1. Deployment Model

Single-server Docker Compose deployment with two containers:

```
┌──────────────────────────────────────────────────────────┐
│                    Host Server (VPS)                       │
│                                                            │
│  ┌────────────────────────┐  ┌─────────────────────────┐  │
│  │      app container      │  │  litestream container    │  │
│  │                         │  │                          │  │
│  │  ASP.NET 10 (Kestrel)  │  │  Litestream daemon       │  │
│  │  Port 8080              │  │  Watches all .db files   │  │
│  │                         │  │  Replicates to R2        │  │
│  └──────────┬──────────────┘  └────────────┬─────────────┘  │
│             │                               │                │
│             ▼                               ▼                │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              Shared Volume: /app/data                │    │
│  │                                                      │    │
│  │  core.db          (platform data)                    │    │
│  │  audit.db         (audit log)                        │    │
│  │  keys/            (data protection keys)             │    │
│  │  tenants/                                            │    │
│  │    ├── acme.db                                       │    │
│  │    ├── globex.db                                     │    │
│  │    └── ...N tenant databases                         │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │          Reverse Proxy (Caddy / nginx)                │   │
│  │          TLS termination, HTTPS → :8080               │   │
│  └──────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
          │                              ▲
          ▼                              │
  Cloudflare R2 (backup)         Inbound HTTPS traffic
```

The reverse proxy (Caddy recommended for automatic TLS) sits in front but is **not** managed by our docker-compose.yml — it's assumed to be already running on the host or managed separately.

---

## 2. Docker Compose — Production

```yaml
# docker-compose.yml
services:
  app:
    build:
      context: .
      dockerfile: src/Dockerfile
    container_name: saas-app
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
    env_file:
      - .env
    volumes:
      - app-data:/app/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

  litestream:
    image: litestream/litestream:0.3
    container_name: saas-litestream
    restart: unless-stopped
    volumes:
      - app-data:/app/data
      - ./litestream.yml:/etc/litestream.yml:ro
    environment:
      - LITESTREAM_ACCESS_KEY_ID=${R2_ACCESS_KEY_ID}
      - LITESTREAM_SECRET_ACCESS_KEY=${R2_SECRET_ACCESS_KEY}
    entrypoint: litestream
    command: replicate -config /etc/litestream.yml
    depends_on:
      app:
        condition: service_healthy

volumes:
  app-data:
    driver: local
```

### Environment File (.env)

```env
# .env (production — NOT committed to git)

# --- Database ---
ConnectionStrings__CoreDatabase=Data Source=/app/data/core.db
ConnectionStrings__AuditDatabase=Data Source=/app/data/audit.db
TenantDatabasePath=/app/data/tenants

# --- Auth ---
Auth__SuperAdmin__Email=admin@example.com
Auth__MagicLink__TokenExpiryMinutes=15

# --- Billing ---
Billing__Provider=Paystack
Billing__Paystack__SecretKey=sk_live_xxxxx
Billing__Paystack__PublicKey=pk_live_xxxxx
Billing__Paystack__WebhookSecret=whsec_xxxxx
Billing__Paystack__CallbackBaseUrl=https://myapp.com

# --- Email ---
Email__Provider=SES
Email__SES__AccessKeyId=AKIA_xxxxx
Email__SES__SecretAccessKey=xxxxx
Email__SES__Region=eu-west-1
Email__FromAddress=noreply@myapp.com
Email__FromName=MyApp

# --- Bot Protection ---
Turnstile__SiteKey=0x4AAAAAAA_xxxxx
Turnstile__SecretKey=0x4AAAAAAA_xxxxx

# --- Litestream / R2 ---
R2_ACCESS_KEY_ID=xxxxx
R2_SECRET_ACCESS_KEY=xxxxx
R2_ENDPOINT=https://<account-id>.r2.cloudflarestorage.com
R2_BUCKET=saas-backups

# --- Data Protection ---
DataProtection__KeyPath=/app/data/keys
```

### Dockerfile

```dockerfile
# src/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/saas.csproj ./
RUN dotnet restore

COPY src/ ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health check
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Create data directories
RUN mkdir -p /app/data/tenants /app/data/keys

EXPOSE 8080
ENTRYPOINT ["dotnet", "saas.dll"]
```

---

## 3. Litestream — SQLite Replication

[Litestream](https://litestream.io) continuously replicates SQLite WAL changes to Cloudflare R2 (S3-compatible object storage). This provides near-real-time backups without stopping the application.

### Challenge: Dynamic Tenant Databases

Litestream's config file is **static** — it must list every database it replicates. But tenant databases are created dynamically at runtime. The solution: a **config regeneration script** that runs on a schedule.

### Litestream Config Template

```yaml
# litestream.yml
# This file is regenerated by the config sync script whenever tenants change.

dbs:
  # --- Core database (always present) ---
  - path: /app/data/core.db
    replicas:
      - type: s3
        bucket: ${R2_BUCKET}
        path: core.db
        endpoint: ${R2_ENDPOINT}
        force-path-style: true

  # --- Audit database (always present) ---
  - path: /app/data/audit.db
    replicas:
      - type: s3
        bucket: ${R2_BUCKET}
        path: audit.db
        endpoint: ${R2_ENDPOINT}
        force-path-style: true

  # --- Tenant databases (dynamically generated) ---
  # Each tenant DB is replicated to its own R2 prefix
  # Entries below are generated by the sync script
```

### Config Sync — Background Service

A background service in the application detects new tenant databases and regenerates the Litestream config. Litestream is then signalled to reload.

```csharp
public class LitestreamConfigSyncService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LitestreamConfigSyncService> _logger;
    private readonly string _tenantDbPath;
    private readonly string _litestreamConfigPath;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

    public LitestreamConfigSyncService(IConfiguration configuration,
        ILogger<LitestreamConfigSyncService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _tenantDbPath = configuration["TenantDatabasePath"] ?? "/app/data/tenants";
        _litestreamConfigPath = configuration["Litestream:ConfigPath"]
            ?? "/etc/litestream.yml";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncConfigAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync Litestream config");
            }

            await Task.Delay(_syncInterval, stoppingToken);
        }
    }

    private async Task SyncConfigAsync()
    {
        var r2Bucket = _configuration["R2_BUCKET"] ?? "saas-backups";
        var r2Endpoint = _configuration["R2_ENDPOINT"] ?? "";

        // Discover all tenant .db files
        var tenantDbs = Directory.Exists(_tenantDbPath)
            ? Directory.GetFiles(_tenantDbPath, "*.db")
            : [];

        var yaml = new StringBuilder();
        yaml.AppendLine("dbs:");

        // Core database
        AppendDbEntry(yaml, "/app/data/core.db", "core.db", r2Bucket, r2Endpoint);

        // Audit database
        AppendDbEntry(yaml, "/app/data/audit.db", "audit.db", r2Bucket, r2Endpoint);

        // Tenant databases
        foreach (var dbFile in tenantDbs)
        {
            var fileName = Path.GetFileName(dbFile);
            var r2Path = $"tenants/{fileName}";
            AppendDbEntry(yaml, $"/app/data/tenants/{fileName}", r2Path, r2Bucket, r2Endpoint);
        }

        var newConfig = yaml.ToString();

        // Only write if changed
        var existingConfig = File.Exists(_litestreamConfigPath)
            ? await File.ReadAllTextAsync(_litestreamConfigPath)
            : "";

        if (newConfig != existingConfig)
        {
            await File.WriteAllTextAsync(_litestreamConfigPath, newConfig);
            _logger.LogInformation(
                "Litestream config updated with {Count} tenant databases. Sending SIGHUP.",
                tenantDbs.Length);

            // Signal Litestream to reload config (SIGHUP)
            // This works because both containers share a PID namespace or
            // the app writes the file to a shared volume and Litestream watches it
            await SignalLitestreamReloadAsync();
        }
    }

    private static void AppendDbEntry(StringBuilder yaml, string path, string r2Path,
        string bucket, string endpoint)
    {
        yaml.AppendLine($"  - path: {path}");
        yaml.AppendLine($"    replicas:");
        yaml.AppendLine($"      - type: s3");
        yaml.AppendLine($"        bucket: {bucket}");
        yaml.AppendLine($"        path: {r2Path}");
        yaml.AppendLine($"        endpoint: {endpoint}");
        yaml.AppendLine($"        force-path-style: true");
    }

    private async Task SignalLitestreamReloadAsync()
    {
        // Option A: Write a sentinel file that a sidecar script watches
        // Option B: Use Docker API to send SIGHUP to the litestream container
        // Option C: Litestream watches the config file for changes (future feature)

        // For now, write a reload sentinel that a wrapper script in the
        // litestream container monitors:
        var sentinelPath = "/app/data/.litestream-reload";
        await File.WriteAllTextAsync(sentinelPath, DateTime.UtcNow.ToString("O"));
    }
}
```

### Litestream Container Wrapper Script

A small wrapper script in the Litestream container watches for the reload sentinel:

```bash
#!/bin/sh
# litestream-wrapper.sh — entrypoint for the litestream container

CONFIG=/etc/litestream.yml
SENTINEL=/app/data/.litestream-reload

# Start litestream in background
litestream replicate -config $CONFIG &
LITESTREAM_PID=$!

# Watch for config reload signal
LAST_RELOAD=""
while true; do
    if [ -f "$SENTINEL" ]; then
        CURRENT=$(cat "$SENTINEL")
        if [ "$CURRENT" != "$LAST_RELOAD" ]; then
            echo "Config reload requested, restarting litestream..."
            kill $LITESTREAM_PID 2>/dev/null
            wait $LITESTREAM_PID 2>/dev/null
            litestream replicate -config $CONFIG &
            LITESTREAM_PID=$!
            LAST_RELOAD="$CURRENT"
        fi
    fi
    sleep 10
done
```

Update the Litestream service in docker-compose.yml to use this wrapper:

```yaml
  litestream:
    image: litestream/litestream:0.3
    container_name: saas-litestream
    restart: unless-stopped
    volumes:
      - app-data:/app/data
      - ./litestream-wrapper.sh:/usr/local/bin/litestream-wrapper.sh:ro
    environment:
      - LITESTREAM_ACCESS_KEY_ID=${R2_ACCESS_KEY_ID}
      - LITESTREAM_SECRET_ACCESS_KEY=${R2_SECRET_ACCESS_KEY}
    entrypoint: /bin/sh
    command: /usr/local/bin/litestream-wrapper.sh
    depends_on:
      app:
        condition: service_healthy
```

### Restore Procedure

To restore from R2 backup:

```bash
# Stop the application
docker compose down

# Restore core database
litestream restore -config litestream.yml -o /app/data/core.db /app/data/core.db

# Restore a specific tenant database
litestream restore -config litestream.yml \
  -o /app/data/tenants/acme.db /app/data/tenants/acme.db

# Restart
docker compose up -d
```

---

## 4. Cloudflare Turnstile — Bot Protection

[Cloudflare Turnstile](https://developers.cloudflare.com/turnstile/) provides invisible bot protection without CAPTCHAs. Used on registration and login forms.

### IBotProtection Interface

```csharp
// Shared/IBotProtection.cs (defined in 03-modules.md)
public interface IBotProtection
{
    Task<bool> ValidateAsync(string? token);
}
```

### Turnstile Implementation

```csharp
public class TurnstileBotProtection : IBotProtection
{
    private readonly HttpClient _httpClient;
    private readonly TurnstileOptions _options;
    private readonly ILogger<TurnstileBotProtection> _logger;

    public TurnstileBotProtection(HttpClient httpClient,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileBotProtection> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> ValidateAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new
                {
                    secret = _options.SecretKey,
                    response = token
                });

            var result = await response.Content
                .ReadFromJsonAsync<TurnstileVerifyResponse>();

            return result?.Success == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turnstile verification failed");
            return false;
        }
    }
}

public class TurnstileOptions
{
    public string SiteKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}

public class TurnstileVerifyResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error-codes")]
    public List<string>? ErrorCodes { get; set; }
}
```

### Mock Bot Protection (Local Dev)

```csharp
public class MockBotProtection : IBotProtection
{
    private readonly ILogger<MockBotProtection> _logger;

    public MockBotProtection(ILogger<MockBotProtection> logger) => _logger = logger;

    public Task<bool> ValidateAsync(string? token)
    {
        _logger.LogInformation("[MOCK BOT PROTECTION] Auto-passing validation");
        return Task.FromResult(true);
    }
}
```

### Service Registration

```csharp
// In InfrastructureModule or ServiceCollectionExtensions
public static void AddBotProtection(this IServiceCollection services,
    IConfiguration configuration)
{
    var provider = configuration.GetValue<string>("Turnstile:Provider") ?? "Mock";

    if (provider.Equals("Cloudflare", StringComparison.OrdinalIgnoreCase))
    {
        services.Configure<TurnstileOptions>(configuration.GetSection("Turnstile"));
        services.AddHttpClient<IBotProtection, TurnstileBotProtection>();
    }
    else
    {
        services.AddSingleton<IBotProtection, MockBotProtection>();
    }
}
```

### Turnstile Client-Side Widget

In Razor views that require bot protection:

```html
<!-- Turnstile invisible widget -->
<div class="cf-turnstile"
     data-sitekey="@Configuration["Turnstile:SiteKey"]"
     data-callback="onTurnstileVerify"
     data-theme="auto">
</div>

<input type="hidden" name="TurnstileToken" id="turnstile-token" />

<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
<script>
    function onTurnstileVerify(token) {
        document.getElementById('turnstile-token').value = token;
    }
</script>
```

For HTMX forms, include the token in `hx-vals`:

```html
<form hx-post="/register"
      hx-target="#main-content"
      hx-include="#turnstile-token">
    <!-- form fields -->
    <div class="cf-turnstile"
         data-sitekey="@Configuration["Turnstile:SiteKey"]"
         data-callback="onTurnstileVerify">
    </div>
    <input type="hidden" name="TurnstileToken" id="turnstile-token" />
</form>
```

---

## 5. Rate Limiting

ASP.NET Core's built-in rate limiting middleware protects public endpoints from abuse.

### Configuration

```csharp
public static void AddAppRateLimiting(this IServiceCollection services)
{
    services.AddRateLimiter(options =>
    {
        // Global limiter — applies to all requests
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        // Named policy: strict — for auth endpoints (login, magic link request)
        options.AddFixedWindowLimiter("strict", opt =>
        {
            opt.PermitLimit = 5;
            opt.Window = TimeSpan.FromMinutes(1);
            opt.QueueLimit = 0;
        });

        // Named policy: registration — for signup endpoint
        options.AddFixedWindowLimiter("registration", opt =>
        {
            opt.PermitLimit = 3;
            opt.Window = TimeSpan.FromMinutes(5);
            opt.QueueLimit = 0;
        });

        // Named policy: webhook — higher limit for Paystack webhooks
        options.AddFixedWindowLimiter("webhook", opt =>
        {
            opt.PermitLimit = 50;
            opt.Window = TimeSpan.FromMinutes(1);
            opt.QueueLimit = 5;
        });

        // Custom rejection response
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "text/html";
            await context.HttpContext.Response.WriteAsync(
                "<div class='alert alert-warning'>Too many requests. Please wait a moment and try again.</div>",
                cancellationToken);
        };
    });
}
```

### Usage in Middleware Pipeline

```csharp
// Program.cs — after authentication, before routing
app.UseRateLimiter();
```

### Usage on Controllers / Endpoints

```csharp
[EnableRateLimiting("strict")]
[HttpPost("request-magic-link")]
public async Task<IActionResult> RequestMagicLink(string email) { ... }

[EnableRateLimiting("registration")]
[HttpPost("register")]
public async Task<IActionResult> Register(RegisterRequest request) { ... }

[EnableRateLimiting("webhook")]
[HttpPost("api/webhooks/paystack")]
public async Task<IActionResult> HandleWebhook() { ... }
```

---

## 6. Email — AWS SES

### IEmailService Interface

```csharp
// Shared/IEmailService.cs (defined in 03-modules.md)
public interface IEmailService
{
    Task SendAsync(EmailMessage message);
    Task SendMagicLinkAsync(string toEmail, string subject, string magicLinkUrl);
    Task SendWelcomeAsync(string toEmail, string tenantName, string loginUrl);
    Task SendPaymentReceiptAsync(string toEmail, string invoiceNumber, decimal amount,
        string currency);
}

public class EmailMessage
{
    public string ToEmail { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string? TextBody { get; set; }
}
```

### SES Implementation

```csharp
public class SesEmailService : IEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly EmailOptions _options;
    private readonly ILogger<SesEmailService> _logger;

    public SesEmailService(IAmazonSimpleEmailServiceV2 ses,
        IOptions<EmailOptions> options,
        ILogger<SesEmailService> logger)
    {
        _ses = ses;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message)
    {
        try
        {
            var request = new SendEmailRequest
            {
                FromEmailAddress = $"{_options.FromName} <{_options.FromAddress}>",
                Destination = new Destination
                {
                    ToAddresses = [message.ToEmail]
                },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = message.Subject },
                        Body = new Body
                        {
                            Html = new Content { Data = message.HtmlBody },
                            Text = message.TextBody is not null
                                ? new Content { Data = message.TextBody }
                                : null
                        }
                    }
                }
            };

            await _ses.SendEmailAsync(request);
            _logger.LogInformation("Email sent to {Email}: {Subject}",
                message.ToEmail, message.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", message.ToEmail);
            throw;
        }
    }

    public async Task SendMagicLinkAsync(string toEmail, string subject, string magicLinkUrl)
    {
        await SendAsync(new EmailMessage
        {
            ToEmail = toEmail,
            Subject = subject,
            HtmlBody = $"""
                <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                    <h2>Sign In</h2>
                    <p>Click the button below to sign in. This link expires in 15 minutes.</p>
                    <p style="text-align: center; margin: 30px 0;">
                        <a href="{magicLinkUrl}"
                           style="background-color: #570df8; color: white; padding: 12px 24px;
                                  border-radius: 6px; text-decoration: none; font-weight: bold;">
                            Sign In
                        </a>
                    </p>
                    <p style="color: #666; font-size: 14px;">
                        If the button doesn't work, copy and paste this URL:<br/>
                        <a href="{magicLinkUrl}">{magicLinkUrl}</a>
                    </p>
                    <p style="color: #999; font-size: 12px;">
                        If you didn't request this, you can safely ignore this email.
                    </p>
                </div>
                """,
            TextBody = $"Sign in by visiting: {magicLinkUrl}\n\nThis link expires in 15 minutes."
        });
    }

    public async Task SendWelcomeAsync(string toEmail, string tenantName, string loginUrl)
    {
        await SendAsync(new EmailMessage
        {
            ToEmail = toEmail,
            Subject = $"Welcome to {tenantName}!",
            HtmlBody = $"""
                <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                    <h2>Welcome to {tenantName}!</h2>
                    <p>Your account has been set up. Click below to sign in for the first time.</p>
                    <p style="text-align: center; margin: 30px 0;">
                        <a href="{loginUrl}"
                           style="background-color: #570df8; color: white; padding: 12px 24px;
                                  border-radius: 6px; text-decoration: none; font-weight: bold;">
                            Go to Dashboard
                        </a>
                    </p>
                </div>
                """
        });
    }

    public async Task SendPaymentReceiptAsync(string toEmail, string invoiceNumber,
        decimal amount, string currency)
    {
        await SendAsync(new EmailMessage
        {
            ToEmail = toEmail,
            Subject = $"Payment Receipt — {invoiceNumber}",
            HtmlBody = $"""
                <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                    <h2>Payment Received</h2>
                    <p>Thank you for your payment.</p>
                    <table style="width: 100%; border-collapse: collapse; margin: 20px 0;">
                        <tr>
                            <td style="padding: 8px; border-bottom: 1px solid #eee;">Invoice</td>
                            <td style="padding: 8px; border-bottom: 1px solid #eee; text-align: right;">
                                {invoiceNumber}
                            </td>
                        </tr>
                        <tr>
                            <td style="padding: 8px; font-weight: bold;">Amount</td>
                            <td style="padding: 8px; text-align: right; font-weight: bold;">
                                {currency} {amount:N2}
                            </td>
                        </tr>
                    </table>
                </div>
                """
        });
    }
}
```

### Console Email Service (Local Development)

```csharp
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message)
    {
        _logger.LogInformation(
            """
            ╔══════════════════════════════════════════╗
            ║            EMAIL (Console)               ║
            ╠══════════════════════════════════════════╣
            ║ To:      {To}
            ║ Subject: {Subject}
            ╠══════════════════════════════════════════╣
            {Body}
            ╚══════════════════════════════════════════╝
            """,
            message.ToEmail, message.Subject, message.HtmlBody);

        return Task.CompletedTask;
    }

    public Task SendMagicLinkAsync(string toEmail, string subject, string magicLinkUrl)
    {
        _logger.LogInformation(
            "\n★ MAGIC LINK for {Email}: {Url}\n",
            toEmail, magicLinkUrl);

        return Task.CompletedTask;
    }

    public Task SendWelcomeAsync(string toEmail, string tenantName, string loginUrl)
    {
        _logger.LogInformation(
            "\n★ WELCOME EMAIL for {Email} ({Tenant}): {Url}\n",
            toEmail, tenantName, loginUrl);

        return Task.CompletedTask;
    }

    public Task SendPaymentReceiptAsync(string toEmail, string invoiceNumber,
        decimal amount, string currency)
    {
        _logger.LogInformation(
            "\n★ PAYMENT RECEIPT for {Email}: {Invoice} — {Currency} {Amount}\n",
            toEmail, invoiceNumber, currency, amount);

        return Task.CompletedTask;
    }
}
```

### Email Options & Registration

```csharp
public class EmailOptions
{
    public string Provider { get; set; } = "Console"; // "Console" or "SES"
    public string FromAddress { get; set; } = "noreply@localhost";
    public string FromName { get; set; } = "SaaS App";
}

public static void AddEmailService(this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<EmailOptions>(configuration.GetSection("Email"));

    var provider = configuration.GetValue<string>("Email:Provider") ?? "Console";

    if (provider.Equals("SES", StringComparison.OrdinalIgnoreCase))
    {
        // Register AWS SES v2 client
        services.AddAWSService<IAmazonSimpleEmailServiceV2>(
            configuration.GetAWSOptions("Email:SES"));
        services.AddScoped<IEmailService, SesEmailService>();
    }
    else
    {
        services.AddSingleton<IEmailService, ConsoleEmailService>();
    }
}
```

---

## 7. Data Protection Key Persistence

ASP.NET Core Data Protection keys (used for cookie encryption, anti-forgery tokens) must persist across container restarts. By default they're ephemeral — users would be logged out on every deployment.

```csharp
public static void AddAppDataProtection(this IServiceCollection services,
    IConfiguration configuration)
{
    var keyPath = configuration["DataProtection:KeyPath"] ?? "data/keys";
    Directory.CreateDirectory(keyPath);

    services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("saas-app")
        .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
}
```

The `data/keys/` directory lives on the shared Docker volume, so keys survive container recreation.

---

## 8. Response Compression

Already configured in the existing codebase via `WebOptimizerExtensions`. The production setup uses ASP.NET Core's response compression middleware:

```csharp
public static void AddAppCompression(this IServiceCollection services)
{
    services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        [
            "application/javascript",
            "text/css",
            "text/html",
            "application/json",
            "image/svg+xml"
        ]);
    });

    services.Configure<BrotliCompressionProviderOptions>(options =>
        options.Level = CompressionLevel.Fastest);

    services.Configure<GzipCompressionProviderOptions>(options =>
        options.Level = CompressionLevel.Fastest);
}
```

In the pipeline, response compression is the **first** middleware:

```csharp
app.UseResponseCompression(); // First in pipeline
```

---

## 9. Health Check Endpoint

A simple health check endpoint for Docker health checks and monitoring:

```csharp
public static void AddAppHealthChecks(this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddHealthChecks()
        .AddCheck("core_db", () =>
        {
            var corePath = configuration.GetConnectionString("CoreDatabase")
                ?.Replace("Data Source=", "");
            return File.Exists(corePath)
                ? HealthCheckResult.Healthy("Core database exists")
                : HealthCheckResult.Unhealthy("Core database not found");
        })
        .AddCheck("data_directory", () =>
        {
            var tenantsPath = configuration["TenantDatabasePath"] ?? "data/tenants";
            return Directory.Exists(tenantsPath)
                ? HealthCheckResult.Healthy("Tenant data directory exists")
                : HealthCheckResult.Unhealthy("Tenant data directory not found");
        });
}
```

Mapped in the pipeline:

```csharp
app.MapHealthChecks("/health");
```

---

## 10. HTTPS & Security Headers

The reverse proxy handles TLS termination. The app adds security headers:

```csharp
public static void UseSecurityHeaders(this IApplicationBuilder app)
{
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-XSS-Protection"] = "0"; // Deprecated, rely on CSP
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        // Content Security Policy
        headers["Content-Security-Policy"] = string.Join("; ",
            "default-src 'self'",
            "script-src 'self' https://challenges.cloudflare.com",   // Turnstile
            "style-src 'self' 'unsafe-inline'",                      // Tailwind runtime
            "img-src 'self' data:",
            "frame-src https://challenges.cloudflare.com",           // Turnstile iframe
            "connect-src 'self'",
            "font-src 'self'",
            "base-uri 'self'",
            "form-action 'self'"
        );

        await next();
    });
}
```

### Forwarded Headers (Behind Reverse Proxy)

```csharp
// In Program.cs — first middleware
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

---

## 11. Logging

Structured logging with ASP.NET Core's built-in logging. Production logs go to stdout (captured by Docker):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "Microsoft.AspNetCore.Diagnostics": "Warning"
    },
    "Console": {
      "FormatterName": "json",
      "FormatterOptions": {
        "SingleLine": true,
        "IncludeScopes": true,
        "TimestampFormat": "yyyy-MM-dd HH:mm:ss "
      }
    }
  }
}
```

Local development uses plain-text console logging (the default). JSON structured logging is production-only.

---

## 12. Configuration Summary

```json
{
  "ConnectionStrings": {
    "CoreDatabase": "Data Source=data/core.db",
    "AuditDatabase": "Data Source=data/audit.db"
  },
  "TenantDatabasePath": "data/tenants",

  "Auth": {
    "SuperAdmin": { "Email": "admin@example.com" },
    "MagicLink": { "TokenExpiryMinutes": 15 }
  },

  "Billing": {
    "Provider": "Mock",
    "Paystack": {
      "SecretKey": "",
      "PublicKey": "",
      "WebhookSecret": "",
      "CallbackBaseUrl": "https://localhost:5001"
    }
  },

  "Email": {
    "Provider": "Console",
    "FromAddress": "noreply@localhost",
    "FromName": "SaaS App",
    "SES": {
      "AccessKeyId": "",
      "SecretAccessKey": "",
      "Region": "eu-west-1"
    }
  },

  "Turnstile": {
    "Provider": "Mock",
    "SiteKey": "",
    "SecretKey": ""
  },

  "DataProtection": {
    "KeyPath": "data/keys"
  },

  "Litestream": {
    "ConfigPath": "/etc/litestream.yml"
  },

  "FeatureFlags": {
    "AllEnabledLocally": true
  }
}
```

### Provider Toggle Pattern

Every external service follows the same provider toggle pattern:

| Service | Config Key | Local Value | Production Value |
|---------|-----------|-------------|-----------------|
| Billing | `Billing:Provider` | `Mock` | `Paystack` |
| Email | `Email:Provider` | `Console` | `SES` |
| Bot Protection | `Turnstile:Provider` | `Mock` | `Cloudflare` |
| Feature Flags | `FeatureFlags:AllEnabledLocally` | `true` | `false` |

This means **zero external dependencies for local development** — no Paystack account, no AWS credentials, no Cloudflare keys needed to run the app locally.

---

## 13. Middleware Pipeline Order

The complete middleware pipeline in `Program.cs`:

```csharp
// 1. Response compression (first — wraps everything)
app.UseResponseCompression();

// 2. Forwarded headers (for reverse proxy)
app.UseForwardedHeaders();

// 3. Security headers
app.UseSecurityHeaders();

// 4. Exception handling
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/error");

// 5. Static files
app.UseStaticFiles();

// 6. Routing
app.UseRouting();

// 7. Rate limiting
app.UseRateLimiter();

// 8. Tenant resolution (sets ITenantContext for the request)
app.UseTenantResolution();

// 9. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 10. Current user (reads cookie, populates ICurrentUser)
app.UseCurrentUser();

// 11. Health check
app.MapHealthChecks("/health");

// 12. MVC routes
app.MapControllerRoute(...);
```

---

## Next Steps

→ [08 — Local Development](08-local-development.md) for the complete clone-and-run setup guide.
