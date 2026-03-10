using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Infrastructure.Middleware;
using saas.Modules.Branding.Entities;
using saas.Modules.Branding.Services;
using saas.Modules.Billing.Entities;
using saas.Modules.Tenancy.Entities;
using Xunit;

namespace saas.Tests.Modules.Branding;

public class BrandingServiceTests : IAsyncLifetime
{
    private SqliteConnection _tenantConnection = null!;
    private SqliteConnection _coreConnection = null!;
    private TenantDbContext _tenantDb = null!;
    private CoreDbContext _coreDb = null!;
    private BrandingService _service = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        _tenantConnection = new SqliteConnection("Data Source=:memory:");
        _coreConnection = new SqliteConnection("Data Source=:memory:");
        await _tenantConnection.OpenAsync();
        await _coreConnection.OpenAsync();

        _tenantDb = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_tenantConnection).Options);
        _coreDb = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseSqlite(_coreConnection).Options);

        await _tenantDb.Database.EnsureCreatedAsync();
        await _coreDb.Database.EnsureCreatedAsync();

        var plan = new Plan
        {
            Name = "Starter",
            Slug = "starter",
            Description = "Starter",
            Currency = "ZAR",
            BillingModel = BillingModel.FlatRate,
            IsActive = true
        };
        _coreDb.Plans.Add(plan);

        var tenant = new Tenant
        {
            Name = "Demo Workspace",
            Slug = "demo",
            ContactEmail = "hello@demo.local",
            Plan = plan,
            Status = TenantStatus.Active
        };
        _coreDb.Tenants.Add(tenant);
        await _coreDb.SaveChangesAsync();
        _tenantId = tenant.Id;

        var tenantContext = new TenantContext();
        typeof(TenantContext).GetProperty(nameof(TenantContext.TenantId))!.SetValue(tenantContext, _tenantId);
        typeof(TenantContext).GetProperty(nameof(TenantContext.TenantName))!.SetValue(tenantContext, tenant.Name);
        typeof(TenantContext).GetProperty(nameof(TenantContext.Slug))!.SetValue(tenantContext, tenant.Slug);

        _service = new BrandingService(_tenantDb, _coreDb, tenantContext);
    }

    public async Task DisposeAsync()
    {
        await _tenantDb.DisposeAsync();
        await _coreDb.DisposeAsync();
        await _tenantConnection.DisposeAsync();
        await _coreConnection.DisposeAsync();
    }

    [Fact]
    public async Task GetSettingsAsync_CreatesDefaultsAndUsesTenantFallbacks()
    {
        var settings = await _service.GetSettingsAsync();

        Assert.Equal("Demo Workspace", settings.EffectiveAgencyName);
        Assert.Equal("hello@demo.local", settings.EffectiveContactEmail);
        Assert.StartsWith("QT-", settings.PreviewReferenceNumber);
        Assert.Equal("#2563EB", settings.PrimaryColor);
    }

    [Fact]
    public async Task UpdateAsync_PersistsBrandingValuesAndPreview()
    {
        await _service.UpdateAsync(new saas.Modules.Branding.DTOs.BrandingSettingsDto
        {
            AgencyName = "Acacia Journeys",
            PublicContactEmail = "quotes@acacia.test",
            ContactPhone = "+27 11 555 1111",
            Website = "https://acacia.test",
            PrimaryColor = "#0F766E",
            SecondaryColor = "#1E293B",
            QuotePrefix = "ACJ",
            QuoteNumberFormat = "{PREFIX}-{YEAR2}-{SEQ:3}",
            NextQuoteSequence = 7,
            QuoteResetSequenceYearly = false,
            DefaultQuoteValidityDays = 21,
            DefaultQuoteMarkupPercentage = 18m,
            PdfFooterText = "Subject to supplier reconfirmation."
        });

        var settings = await _service.GetSettingsAsync();
        var entity = await _tenantDb.BrandingSettings.SingleAsync();

        Assert.Equal("Acacia Journeys", entity.AgencyName);
        Assert.Equal("quotes@acacia.test", entity.PublicContactEmail);
        Assert.Equal("ACJ", entity.QuotePrefix);
        Assert.Equal(21, entity.DefaultQuoteValidityDays);
        Assert.Equal(18m, entity.DefaultQuoteMarkupPercentage);
        Assert.Contains("ACJ-", settings.PreviewReferenceNumber);
        Assert.Equal("Acacia Journeys", settings.EffectiveAgencyName);
    }
}
