using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Infrastructure.Middleware;
using saas.Modules.Branding.Entities;
using saas.Modules.Billing.Entities;
using saas.Modules.Onboarding.DTOs;
using saas.Modules.Onboarding.Entities;
using saas.Modules.Onboarding.Services;
using saas.Modules.Tenancy.Entities;
using Xunit;

namespace saas.Tests.Modules.Onboarding;

public class OnboardingServiceTests : IAsyncLifetime
{
    private SqliteConnection _tenantConnection = null!;
    private SqliteConnection _coreConnection = null!;
    private TenantDbContext _tenantDb = null!;
    private CoreDbContext _coreDb = null!;
    private OnboardingService _service = null!;
    private TenantContext _tenantContext = null!;

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

        _tenantContext = new TenantContext();
        typeof(TenantContext).GetProperty(nameof(TenantContext.TenantId))!.SetValue(_tenantContext, tenant.Id);
        typeof(TenantContext).GetProperty(nameof(TenantContext.TenantName))!.SetValue(_tenantContext, tenant.Name);
        typeof(TenantContext).GetProperty(nameof(TenantContext.Slug))!.SetValue(_tenantContext, tenant.Slug);

        _service = new OnboardingService(_tenantDb, _coreDb, _tenantContext);
    }

    public async Task DisposeAsync()
    {
        await _tenantDb.DisposeAsync();
        await _coreDb.DisposeAsync();
        await _tenantConnection.DisposeAsync();
        await _coreConnection.DisposeAsync();
    }

    [Fact]
    public async Task ShouldRedirectToOnboardingAsync_ReturnsTrue_WhenStateMissing()
    {
        var shouldRedirect = await _service.ShouldRedirectToOnboardingAsync();

        Assert.True(shouldRedirect);
    }

    [Fact]
    public async Task SaveIdentityAsync_PersistsBrandingAndAdvancesStep()
    {
        await _service.SaveIdentityAsync(new OnboardingIdentityStepDto
        {
            AgencyName = "Acacia Journeys",
            PublicContactEmail = "quotes@acacia.test",
            ContactPhone = "+27 11 555 1111",
            Website = "https://acacia.test",
            LogoUrl = "https://cdn.acacia.test/logo.svg",
            PrimaryColor = "#0F766E",
            SecondaryColor = "#1E293B"
        });

        var state = await _tenantDb.TenantOnboardingStates.SingleAsync();
        var branding = await _tenantDb.BrandingSettings.SingleAsync();
        var page = await _service.GetPageAsync();

        Assert.NotNull(state.IdentityCompletedAt);
        Assert.Equal(2, state.CurrentStep);
        Assert.Equal("Acacia Journeys", branding.AgencyName);
        Assert.Equal("quotes@acacia.test", branding.PublicContactEmail);
        Assert.Equal(2, page.CurrentStep);
        Assert.Equal("Acacia Journeys", page.Preview.EffectiveAgencyName);
    }

    [Fact]
    public async Task SaveDefaultsAsync_PersistsQuoteDefaultsAndPreferredWorkspace()
    {
        await _service.SaveIdentityAsync(new OnboardingIdentityStepDto());

        await _service.SaveDefaultsAsync(new OnboardingDefaultsStepDto
        {
            QuotePrefix = "ACJ",
            DefaultQuoteValidityDays = 21,
            DefaultQuoteMarkupPercentage = 18m,
            QuoteResetSequenceYearly = false,
            PreferredWorkspace = OnboardingWorkspaceOptions.RateCards
        });

        var state = await _tenantDb.TenantOnboardingStates.SingleAsync();
        var branding = await _tenantDb.BrandingSettings.SingleAsync();
        var page = await _service.GetPageAsync();

        Assert.NotNull(state.DefaultsCompletedAt);
        Assert.Equal(3, state.CurrentStep);
        Assert.Equal(OnboardingWorkspaceOptions.RateCards, state.PreferredWorkspace);
        Assert.Equal("ACJ", branding.QuotePrefix);
        Assert.Equal(21, branding.DefaultQuoteValidityDays);
        Assert.Equal(18m, branding.DefaultQuoteMarkupPercentage);
        Assert.Equal("Rate Cards", page.Preview.PreferredWorkspaceLabel);
    }

    [Fact]
    public async Task CompleteAsync_MarksCompleted_AndStopsRedirecting()
    {
        await _service.SaveIdentityAsync(new OnboardingIdentityStepDto());
        await _service.SaveDefaultsAsync(new OnboardingDefaultsStepDto());

        await _service.CompleteAsync();

        var state = await _tenantDb.TenantOnboardingStates.SingleAsync();
        var shouldRedirect = await _service.ShouldRedirectToOnboardingAsync();

        Assert.NotNull(state.CompletedAt);
        Assert.False(shouldRedirect);
    }
}
