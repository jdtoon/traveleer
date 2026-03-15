using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Infrastructure.Services;
using saas.Modules.Branding.Entities;
using saas.Modules.Bookings.DTOs;
using saas.Modules.Bookings.Entities;
using saas.Modules.Bookings.Services;
using saas.Modules.Clients.Entities;
using saas.Modules.Communications.Services;
using saas.Modules.Inventory.Entities;
using saas.Modules.Quotes.Entities;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;
using saas.Shared;
using Xunit;

namespace saas.Tests.Modules.Bookings;

public class BookingServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private TenantDbContext _db = null!;
    private BookingService _service = null!;
    private FakeEmailService _emailService = null!;
    private FakeEmailTemplateService _templateService = null!;
    private FakeTenantContext _tenantContext = null!;
    private FakeBookingVoucherDocumentService _voucherDocumentService = null!;
    private Client _client = null!;
    private InventoryItem _hotel = null!;
    private Quote _acceptedQuote = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        var supplier = new Supplier { Name = "Al Haram Hotels", ContactName = "Reservations", ContactEmail = "reservations@alharam.test", IsActive = true, CreatedAt = DateTime.UtcNow };
        var destination = new Destination { Name = "Makkah", IsActive = true, SortOrder = 10, CreatedAt = DateTime.UtcNow };
        _client = new Client { Name = "Acacia Travel Group", Email = "hello@test.com", CreatedAt = DateTime.UtcNow };
        _hotel = new InventoryItem
        {
            Name = "Grand Haram Hotel",
            Kind = InventoryItemKind.Hotel,
            BaseCost = 500m,
            Supplier = supplier,
            Destination = destination,
            CreatedAt = DateTime.UtcNow
        };

        _db.Clients.Add(_client);
        _db.InventoryItems.Add(_hotel);
        var rateCard = new RateCard
        {
            Name = "Grand Haram Contract",
            InventoryItem = _hotel,
            ContractCurrencyCode = "USD",
            Status = RateCardStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _acceptedQuote = new Quote
        {
            ReferenceNumber = "QT-BKG-0001",
            ClientId = _client.Id,
            ClientName = _client.Name,
            ClientEmail = _client.Email,
            OutputCurrencyCode = "USD",
            MarkupPercentage = 20m,
            Status = QuoteStatus.Accepted,
            TravelStartDate = new DateOnly(2026, 7, 10),
            TravelEndDate = new DateOnly(2026, 7, 14),
            Notes = "Client requested airport meet-and-greet.",
            InternalNotes = "Preferred supplier for this client.",
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

        _db.Quotes.Add(_acceptedQuote);
        _db.Currencies.AddRange(
            new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", ExchangeRate = 1m, IsBaseCurrency = true, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Currency { Code = "SAR", Name = "Saudi Riyal", Symbol = "SAR", ExchangeRate = 3.75m, IsBaseCurrency = false, IsActive = true, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        _emailService = new FakeEmailService();
        _templateService = new FakeEmailTemplateService();
        _tenantContext = new FakeTenantContext();
        _voucherDocumentService = new FakeBookingVoucherDocumentService();
        _service = new BookingService(_db, _emailService, _templateService, _tenantContext, _voucherDocumentService, new FakeCommunicationService());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CreateEmptyAsync_LoadsClientAndCurrencyOptions()
    {
        var dto = await _service.CreateEmptyAsync();

        Assert.Contains(dto.ClientOptions, x => x.Label == _client.Name);
        Assert.Contains("USD", dto.CurrencyOptions);
    }

    [Fact]
    public async Task CreateAsync_GeneratesIncrementingBookingReference()
    {
        var firstId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        var secondId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 4
        });

        var first = await _db.Bookings.SingleAsync(x => x.Id == firstId);
        var second = await _db.Bookings.SingleAsync(x => x.Id == secondId);
        Assert.EndsWith("0001", first.BookingRef);
        Assert.EndsWith("0002", second.BookingRef);
    }

    [Fact]
    public async Task ConvertFromQuoteAsync_CreatesBookingLinkedToQuote()
    {
        var result = await _service.ConvertFromQuoteAsync(_acceptedQuote.Id);

        var booking = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.QuoteId == _acceptedQuote.Id);
        var item = Assert.Single(booking.Items);

        Assert.True(result.Success);
        Assert.False(result.AlreadyExists);
        Assert.Equal(booking.Id, result.BookingId);
        Assert.Equal(_acceptedQuote.ReferenceNumber, booking.ClientReference);
        Assert.Equal(_acceptedQuote.InternalNotes, booking.InternalNotes);
        Assert.Equal(_acceptedQuote.Notes, booking.SpecialRequests);
        Assert.Equal(500m, item.CostPrice);
        Assert.Equal(600m, item.SellingPrice);
        Assert.Equal(4, item.Nights);
    }

    [Fact]
    public async Task ConvertFromQuoteAsync_WhenBookingAlreadyExists_ReturnsExistingBooking()
    {
        var first = await _service.ConvertFromQuoteAsync(_acceptedQuote.Id);
        var second = await _service.ConvertFromQuoteAsync(_acceptedQuote.Id);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(second.AlreadyExists);
        Assert.Equal(first.BookingId, second.BookingId);
        Assert.Equal(1, await _db.Bookings.CountAsync(x => x.QuoteId == _acceptedQuote.Id));
    }

    [Fact]
    public async Task ConvertFromQuoteAsync_WhenQuoteIsNotAccepted_Fails()
    {
        _acceptedQuote.Status = QuoteStatus.Sent;
        await _db.SaveChangesAsync();

        var result = await _service.ConvertFromQuoteAsync(_acceptedQuote.Id);

        Assert.False(result.Success);
        Assert.Equal("Only accepted quotes can be converted to bookings.", result.ErrorMessage);
        Assert.Equal(0, await _db.Bookings.CountAsync(x => x.QuoteId == _acceptedQuote.Id));
    }

    [Fact]
    public async Task AddItemAsync_UsesInventoryBaseCostAndRecalculatesTotals()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 4, 10),
            EndDate = new DateOnly(2026, 4, 13),
            Quantity = 2,
            Pax = 2,
            SellingPrice = 650m,
            SupplierReference = "SUP-001"
        });

        var booking = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.Id == bookingId);
        var item = Assert.Single(booking.Items);
        Assert.Equal(500m, item.CostPrice);
        Assert.Equal(650m, item.SellingPrice);
        Assert.Equal(3, item.Nights);
        Assert.Equal(1000m, booking.TotalCost);
        Assert.Equal(1300m, booking.TotalSelling);
        Assert.Equal(300m, booking.TotalProfit);
    }

    [Fact]
    public async Task UpdateItemStatusAsync_WhenAllItemsConfirmed_ConfirmsBooking()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 3),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var booking = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.Id == bookingId);
        var item = booking.Items.Single();

        await _service.UpdateItemStatusAsync(bookingId, item.Id, SupplierStatus.Requested);
        await _service.UpdateItemStatusAsync(bookingId, item.Id, SupplierStatus.Confirmed);

        var updated = await _db.Bookings.Include(x => x.Items).SingleAsync(x => x.Id == bookingId);
        Assert.Equal(BookingStatus.Confirmed, updated.Status);
        Assert.NotNull(updated.ConfirmedAt);
        Assert.Equal(SupplierStatus.Confirmed, updated.Items.Single().SupplierStatus);
    }

    [Fact]
    public async Task SendSupplierRequestAsync_WhenValid_SendsEmailAndMarksItemRequested()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 3),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m,
            SupplierReference = "SUP-REQ-1"
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();

        var result = await _service.SendSupplierRequestAsync(bookingId, itemId);

        var item = await _db.BookingItems.SingleAsync(x => x.Id == itemId);
        Assert.True(result.Success);
        Assert.Equal(SupplierStatus.Requested, item.SupplierStatus);
        Assert.NotNull(item.RequestedAt);
        Assert.Single(_emailService.Messages);
        Assert.Contains("Booking request", _emailService.Messages[0].Subject);
        Assert.Contains("BK-", _emailService.Messages[0].Subject);
    }

    [Fact]
    public async Task SendSupplierRequestAsync_WhenReminder_SendsSecondEmailWithoutResettingRequestedAt()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 3),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();
        await _service.SendSupplierRequestAsync(bookingId, itemId);
        var firstRequestedAt = await _db.BookingItems.Where(x => x.Id == itemId).Select(x => x.RequestedAt).SingleAsync();

        var result = await _service.SendSupplierRequestAsync(bookingId, itemId, isReminder: true);

        var item = await _db.BookingItems.SingleAsync(x => x.Id == itemId);
        Assert.True(result.Success);
        Assert.Equal(SupplierStatus.Requested, item.SupplierStatus);
        Assert.Equal(firstRequestedAt, item.RequestedAt);
        Assert.Equal(2, _emailService.Messages.Count);
        Assert.Contains("Reminder:", _emailService.Messages[1].Subject);
    }

    [Fact]
    public async Task SendSupplierRequestAsync_WhenSupplierEmailMissing_Fails()
    {
        var supplier = await _db.Suppliers.SingleAsync();
        supplier.ContactEmail = null;
        await _db.SaveChangesAsync();

        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 3),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();

        var result = await _service.SendSupplierRequestAsync(bookingId, itemId);

        var item = await _db.BookingItems.SingleAsync(x => x.Id == itemId);
        Assert.False(result.Success);
        Assert.Equal("Add a supplier contact email before sending a supplier request.", result.ErrorMessage);
        Assert.Equal(SupplierStatus.NotRequested, item.SupplierStatus);
        Assert.Empty(_emailService.Messages);
    }

    [Fact]
    public async Task GenerateVoucherAsync_WhenItemConfirmed_AssignsVoucherMetadata()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 4),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Requested);
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Confirmed);

        var result = await _service.GenerateVoucherAsync(bookingId, itemId);

        var item = await _db.BookingItems.SingleAsync(x => x.Id == itemId);
        Assert.True(result.Success);
        Assert.True(item.VoucherGenerated);
        Assert.NotNull(item.VoucherGeneratedAt);
        Assert.StartsWith("V-", item.VoucherNumber);
    }

    [Fact]
    public async Task GenerateVoucherAsync_WhenItemNotConfirmed_Fails()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 4),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();

        var result = await _service.GenerateVoucherAsync(bookingId, itemId);

        Assert.False(result.Success);
        Assert.Equal("Voucher can only be generated after supplier confirmation.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetVoucherPdfAsync_WhenVoucherGenerated_ReturnsPdfBytes()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2,
            LeadGuestName = "Layla Ahmed"
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 4),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m,
            SupplierReference = "SUP-VCH-1"
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Requested);
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Confirmed);
        await _service.GenerateVoucherAsync(bookingId, itemId);

        var pdf = await _service.GetVoucherPdfAsync(bookingId, itemId);

        Assert.NotNull(pdf);
        Assert.Equal("application/pdf", _voucherDocumentService.LastMimeType);
        Assert.StartsWith("V-", Path.GetFileNameWithoutExtension(pdf?.FileName));
        Assert.NotEmpty(pdf?.PdfBytes ?? []);
    }

    [Fact]
    public async Task SendVoucherAsync_WhenVoucherGenerated_SendsPdfAttachmentAndMarksItemSent()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2,
            LeadGuestName = "Layla Ahmed"
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 4),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m,
            SupplierReference = "SUP-VCH-SEND"
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Requested);
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Confirmed);
        await _service.GenerateVoucherAsync(bookingId, itemId);

        var result = await _service.SendVoucherAsync(bookingId, itemId);

        var item = await _db.BookingItems.SingleAsync(x => x.Id == itemId);
        Assert.True(result.Success);
        Assert.True(item.VoucherSent);
        Assert.NotNull(item.VoucherSentAt);
        var email = Assert.Single(_emailService.Messages);
        Assert.Contains("Service voucher", email.Subject);
        var attachment = Assert.Single(email.Attachments ?? []);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.EndsWith(".pdf", attachment.FileName);
        Assert.NotEmpty(attachment.Content);
    }

    [Fact]
    public async Task SendVoucherAsync_WhenVoucherNotGenerated_Fails()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            SellingCurrencyCode = "USD",
            Pax = 2
        });

        await _service.AddItemAsync(bookingId, new BookingItemFormDto
        {
            BookingId = bookingId,
            InventoryItemId = _hotel.Id,
            ServiceDate = new DateOnly(2026, 5, 1),
            EndDate = new DateOnly(2026, 5, 4),
            Quantity = 1,
            Pax = 2,
            SellingPrice = 600m
        });

        var itemId = await _db.BookingItems.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync();
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Requested);
        await _service.UpdateItemStatusAsync(bookingId, itemId, SupplierStatus.Confirmed);

        var result = await _service.SendVoucherAsync(bookingId, itemId);

        Assert.False(result.Success);
        Assert.Equal("Generate the voucher before sending it.", result.ErrorMessage);
        Assert.Empty(_emailService.Messages);
    }

    [Fact]
    public async Task GetListAsync_SearchMatchesBookingReferenceAndClient()
    {
        var bookingId = await _service.CreateAsync(new BookingFormDto
        {
            ClientId = _client.Id,
            ClientReference = "VIP-OPS",
            SellingCurrencyCode = "USD",
            Pax = 3
        });

        var booking = await _db.Bookings.SingleAsync(x => x.Id == bookingId);

        var byRef = await _service.GetListAsync(search: booking.BookingRef);
        var byClient = await _service.GetListAsync(search: _client.Name);

        Assert.Single(byRef.Items);
        Assert.Single(byClient.Items);
        Assert.Equal(bookingId, byRef.Items[0].Id);
        Assert.Equal(bookingId, byClient.Items[0].Id);
    }

    private sealed class FakeEmailService : IEmailService
    {
        public List<EmailMessage> Messages { get; } = [];
        public EmailSendResult NextResult { get; set; } = EmailSendResult.Succeeded();

        public Task<EmailSendResult> SendAsync(EmailMessage message)
        {
            Messages.Add(message);
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

    private sealed class FakeTenantContext : ITenantContext
    {
        public string? Slug => "demo";
        public Guid? TenantId => Guid.NewGuid();
        public string? PlanSlug => "starter";
        public string? TenantName => "Acacia Journeys";
        public bool IsTenantRequest => true;
    }

    private sealed class FakeBookingVoucherDocumentService : IBookingVoucherDocumentService
    {
        public string LastMimeType { get; private set; } = string.Empty;

        public byte[] Generate(Booking booking, BookingItem item, BrandingSettings branding, string agencyName)
        {
            LastMimeType = "application/pdf";
            return System.Text.Encoding.ASCII.GetBytes($"PDF:{booking.BookingRef}:{item.VoucherNumber}:{agencyName}");
        }
    }

    private sealed class FakeCommunicationService : ICommunicationService
    {
        public Task<saas.Modules.Communications.DTOs.CommunicationListDto> GetByClientAsync(Guid clientId, int page = 1, int pageSize = 20)
            => Task.FromResult(new saas.Modules.Communications.DTOs.CommunicationListDto());
        public Task<saas.Modules.Communications.DTOs.CommunicationListDto> GetByBookingAsync(Guid bookingId, int page = 1, int pageSize = 20)
            => Task.FromResult(new saas.Modules.Communications.DTOs.CommunicationListDto());
        public Task<saas.Modules.Communications.DTOs.CommunicationListDto> GetBySupplierAsync(Guid supplierId, int page = 1, int pageSize = 20)
            => Task.FromResult(new saas.Modules.Communications.DTOs.CommunicationListDto());
        public Task<saas.Modules.Communications.DTOs.CommunicationEntryDto?> GetByIdAsync(Guid id)
            => Task.FromResult<saas.Modules.Communications.DTOs.CommunicationEntryDto?>(null);
        public Task<saas.Modules.Communications.Entities.CommunicationEntry> CreateAsync(saas.Modules.Communications.DTOs.CreateCommunicationDto dto)
            => Task.FromResult(new saas.Modules.Communications.Entities.CommunicationEntry());
        public Task UpdateAsync(Guid id, saas.Modules.Communications.DTOs.UpdateCommunicationDto dto) => Task.CompletedTask;
        public Task DeleteAsync(Guid id) => Task.CompletedTask;
        public Task AutoLogEmailAsync(Guid? clientId, Guid? supplierId, Guid? bookingId, string subject, string recipient) => Task.CompletedTask;
    }
}
