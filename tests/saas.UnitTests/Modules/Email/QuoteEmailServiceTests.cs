using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure.Services;
using saas.Modules.Email.DTOs;
using saas.Modules.Email.Entities;
using saas.Modules.Email.Services;
using saas.Modules.Quotes.Entities;
using saas.Modules.Settings.Entities;
using saas.Modules.Branding.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.RateCards.Entities;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Email;

public class QuoteEmailServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private FakeEmailService _emailService = null!;
    private FakeEmailTemplateService _templateService = null!;
    private FakeCurrentUser _currentUser = null!;
    private FakeTenantContext _tenantContext = null!;
    private QuoteEmailService _service = null!;
    private Quote _quote = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _db.Database.EnsureCreatedAsync();

        _emailService = new FakeEmailService();
        _templateService = new FakeEmailTemplateService();
        _currentUser = new FakeCurrentUser();
        _tenantContext = new FakeTenantContext();
        _service = new QuoteEmailService(_db, _emailService, _templateService, _currentUser, _tenantContext);

        var destination = new Destination { Name = "Makkah", SortOrder = 10, IsActive = true, CreatedAt = DateTime.UtcNow };
        var supplier = new Supplier { Name = "Haram Supplier", IsActive = true, CreatedAt = DateTime.UtcNow };
        var hotel = new InventoryItem
        {
            Name = "Grand Haram Hotel",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 2000m,
            Destination = destination,
            Supplier = supplier,
            CreatedAt = DateTime.UtcNow
        };
        var rateCard = new RateCard
        {
            Name = "Main Contract",
            InventoryItem = hotel,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        var client = new Client
        {
            Name = "Acacia Travel Group",
            Email = "quotes@acacia.test",
            Phone = "+27 11 555 1111",
            CreatedAt = DateTime.UtcNow
        };

        _quote = new Quote
        {
            ReferenceNumber = "ACJ-2026-0001",
            Client = client,
            ClientName = client.Name,
            ClientEmail = client.Email,
            ClientPhone = client.Phone,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 15m,
            Status = QuoteStatus.Draft,
            ValidUntil = new DateOnly(2026, 10, 31),
            TravelStartDate = new DateOnly(2026, 10, 20),
            TravelEndDate = new DateOnly(2026, 10, 28),
            Notes = "Airport transfers are included.",
            CreatedAt = DateTime.UtcNow,
            QuoteRateCards =
            {
                new QuoteRateCard
                {
                    RateCard = rateCard,
                    SortOrder = 1
                }
            }
        };

        _db.BrandingSettings.Add(new BrandingSettings
        {
            AgencyName = "Acacia Journeys",
            PublicContactEmail = "sales@acacia.test",
            ContactPhone = "+27 11 555 2222",
            Website = "https://acacia.test",
            PdfFooterText = "Thank you for travelling with us."
        });
        _db.Quotes.Add(_quote);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetComposeAsync_UsesQuoteAndBrandingDefaults()
    {
        var compose = await _service.GetComposeAsync(_quote.Id);

        Assert.NotNull(compose);
        Assert.Equal(_quote.ClientEmail, compose!.ToEmail);
        Assert.Contains(_quote.ReferenceNumber, compose.Subject);
        Assert.Equal("Acacia Journeys", compose.AgencyName);
        Assert.Equal("sales@acacia.test", compose.ReplyToEmail);
        Assert.Equal("20 Oct 2026 - 28 Oct 2026", compose.TravelWindowLabel);
    }

    [Fact]
    public async Task SendQuoteAsync_OnSuccess_LogsAndMarksQuoteSent()
    {
        var result = await _service.SendQuoteAsync(_quote.Id, new QuoteEmailComposeDto
        {
            QuoteId = _quote.Id,
            ToEmail = "client@example.com",
            Subject = "Your Acacia quote",
            CustomMessage = "Please review the attached options."
        });

        var log = await _db.QuoteEmailLogs.SingleAsync();
        var quote = await _db.Quotes.SingleAsync(x => x.Id == _quote.Id);

        Assert.True(result.Success);
        Assert.Equal(QuoteEmailDeliveryStatus.Sent, log.Status);
        Assert.Equal("client@example.com", log.ToEmail);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.NotNull(_emailService.LastMessage);
        Assert.Contains("Acacia Journeys", _emailService.LastMessage!.HtmlBody);
    }

    [Fact]
    public async Task SendQuoteAsync_OnFailure_LogsFailureAndKeepsDraftStatus()
    {
        _emailService.NextResult = EmailSendResult.Failed("SMTP offline");

        var result = await _service.SendQuoteAsync(_quote.Id, new QuoteEmailComposeDto
        {
            QuoteId = _quote.Id,
            ToEmail = "client@example.com",
            Subject = "Your Acacia quote"
        });

        var log = await _db.QuoteEmailLogs.SingleAsync();
        var quote = await _db.Quotes.SingleAsync(x => x.Id == _quote.Id);

        Assert.False(result.Success);
        Assert.Equal(QuoteEmailDeliveryStatus.Failed, log.Status);
        Assert.Equal("SMTP offline", log.ErrorMessage);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
    }

    private sealed class FakeEmailService : IEmailService
    {
        public EmailMessage? LastMessage { get; private set; }
        public EmailSendResult NextResult { get; set; } = EmailSendResult.Succeeded();

        public Task<EmailSendResult> SendAsync(EmailMessage message)
        {
            LastMessage = message;
            return Task.FromResult(NextResult);
        }

        public Task<EmailSendResult> SendMagicLinkAsync(string to, string magicLinkUrl)
            => Task.FromResult(EmailSendResult.Succeeded());
    }

    private sealed class FakeEmailTemplateService : IEmailTemplateService
    {
        public string Render(string templateName, Dictionary<string, string> variables)
            => $"{templateName}:{string.Join("|", variables.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"))}";
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public string? UserId => "user-1";
        public string? Email => "agent@acacia.test";
        public string? DisplayName => "Acacia Agent";
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["Admin"];
        public IReadOnlyList<string> Permissions => ["email.send", "email.read"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool HasAnyPermission(params string[] permissions) => permissions.Any(HasPermission);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public string? Slug => "demo";
        public Guid? TenantId => Guid.NewGuid();
        public string? PlanSlug => "starter";
        public string? TenantName => "Demo Workspace";
        public bool IsTenantRequest => true;
    }
}
