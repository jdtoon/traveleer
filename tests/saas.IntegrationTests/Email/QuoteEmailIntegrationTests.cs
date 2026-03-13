using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.IntegrationTests.Fixtures;
using saas.Modules.Email.Entities;
using saas.Modules.Quotes.Entities;
using Swap.Testing;
using Xunit;

namespace saas.IntegrationTests.Email;

public class QuoteEmailIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;
    private const string TenantSlug = "demo";

    public QuoteEmailIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient(TenantSlug);
    }

    [Fact]
    public async Task QuoteDetailsPage_RendersEmailHistoryContainer()
    {
        var quoteId = await SeedQuoteAsync();

        var response = await _client.GetAsync($"/{TenantSlug}/quotes/details/{quoteId}");

        response.AssertSuccess();
        await response.AssertContainsAsync("<html");
        await response.AssertElementExistsAsync("#quote-email-history");
        await response.AssertContainsAsync("Send Email");
    }

    [Fact]
    public async Task QuoteEmailComposePartial_RendersWithoutLayout()
    {
        var quoteId = await SeedQuoteAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quote-email/compose/{quoteId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Send Quote Email");
        await response.AssertContainsAsync("We prefilled the saved quote email, but you can override it before sending.");
    }

    [Fact]
    public async Task QuoteEmailComposePartial_WhenQuoteHasNoClientEmail_ShowsManualAddressGuidance()
    {
        var quoteId = await SeedQuoteWithoutClientEmailAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quote-email/compose/{quoteId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("No client email is saved on this quote yet, so enter the delivery address manually.");
    }

    [Fact]
    public async Task QuoteEmailHistoryPartial_RendersWithoutLayout()
    {
        var quoteId = await SeedQuoteAsync();

        var response = await _client.HtmxGetAsync($"/{TenantSlug}/quote-email/history/{quoteId}");

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertContainsAsync("Email History");
    }

    [Fact]
    public async Task QuoteEmail_UserCanSendEmail_FromModal()
    {
        var quoteId = await SeedQuoteAsync();
        var compose = await _client.HtmxGetAsync($"/{TenantSlug}/quote-email/compose/{quoteId}");
        compose.AssertSuccess();

        var response = await _client.SubmitFormAsync(compose, "form", new Dictionary<string, string>
        {
            ["QuoteId"] = quoteId.ToString(),
            ["ToEmail"] = "traveler@example.com",
            ["Subject"] = "Your updated quote",
            ["CustomMessage"] = "Everything is ready for your review."
        });

        response.AssertSuccess();
        response.AssertToast("Quote email sent.");
        response.AssertTrigger("email.quote.refresh");
        response.AssertTrigger("quotes.refresh");
        response.AssertTrigger("quotes.details.refresh");

        await using var db = OpenTenantDb();
        var log = await db.QuoteEmailLogs.OrderByDescending(x => x.CreatedAt).FirstAsync(x => x.QuoteId == quoteId);
        var quote = await db.Quotes.SingleAsync(x => x.Id == quoteId);

        Assert.Equal(QuoteEmailDeliveryStatus.Sent, log.Status);
        Assert.Equal("traveler@example.com", log.ToEmail);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
    }

    [Fact]
    public async Task QuoteEmail_OnInvalidSubmit_ReturnsModalWithErrors()
    {
        var quoteId = await SeedQuoteAsync();
        var compose = await _client.HtmxGetAsync($"/{TenantSlug}/quote-email/compose/{quoteId}");
        compose.AssertSuccess();

        var response = await _client.SubmitFormAsync(compose, "form", new Dictionary<string, string>
        {
            ["QuoteId"] = quoteId.ToString(),
            ["ToEmail"] = "not-an-email",
            ["Subject"] = string.Empty
        });

        response.AssertSuccess();
        await response.AssertPartialViewAsync();
        await response.AssertElementExistsAsync("dialog.modal");
        await response.AssertContainsAsync("Enter a valid email address.");
        await response.AssertContainsAsync("Subject is required.");
    }

    [Fact]
    public async Task QuoteEmailCompose_WhenUnauthenticated_Redirects()
    {
        var quoteId = await SeedQuoteAsync();
        var publicClient = _fixture.CreateClient();

        var response = await publicClient.GetAsync($"/{TenantSlug}/quote-email/compose/{quoteId}");

        response.AssertStatus(System.Net.HttpStatusCode.Redirect);
    }

    private async Task<Guid> SeedQuoteAsync()
    {
        await using var db = OpenTenantDb();
        var existing = await db.Quotes.FirstOrDefaultAsync(x => x.ReferenceNumber == "QT-EMAIL-0001");
        if (existing is not null)
        {
            return existing.Id;
        }

        var client = await db.Clients.OrderBy(x => x.Name).FirstAsync();
        var rateCardId = await db.RateCards.OrderBy(x => x.CreatedAt).Select(x => x.Id).FirstAsync();

        var quote = new Quote
        {
            ReferenceNumber = "QT-EMAIL-0001",
            ClientId = client.Id,
            ClientName = client.Name,
            ClientEmail = client.Email,
            ClientPhone = client.Phone,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 10m,
            Status = QuoteStatus.Draft,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(14)),
            CreatedAt = DateTime.UtcNow,
            QuoteRateCards =
            {
                new QuoteRateCard
                {
                    RateCardId = rateCardId,
                    SortOrder = 1
                }
            }
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    }

    private async Task<Guid> SeedQuoteWithoutClientEmailAsync()
    {
        await using var db = OpenTenantDb();
        var reference = $"QT-EMAIL-{Guid.NewGuid():N}"[..13];
        var client = await db.Clients.OrderBy(x => x.Name).FirstAsync();
        var rateCardId = await db.RateCards.OrderBy(x => x.CreatedAt).Select(x => x.Id).FirstAsync();

        var quote = new Quote
        {
            ReferenceNumber = reference,
            ClientId = client.Id,
            ClientName = client.Name,
            ClientEmail = null,
            ClientPhone = client.Phone,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 10m,
            Status = QuoteStatus.Draft,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(14)),
            CreatedAt = DateTime.UtcNow,
            QuoteRateCards =
            {
                new QuoteRateCard
                {
                    RateCardId = rateCardId,
                    SortOrder = 1
                }
            }
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    }

    private TenantDbContext OpenTenantDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
            .Options;
        return new TenantDbContext(options);
    }
}
