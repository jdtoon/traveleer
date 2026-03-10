using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Modules.Branding.Entities;
using saas.Modules.Branding.Services;
using saas.Modules.Onboarding.DTOs;
using saas.Modules.Onboarding.Entities;
using saas.Shared;

namespace saas.Modules.Onboarding.Services;

public interface IOnboardingService
{
    Task<OnboardingPageDto> GetPageAsync(int? requestedStep = null);
    Task<OnboardingIdentityStepDto> GetIdentityStepAsync();
    Task<OnboardingDefaultsStepDto> GetDefaultsStepAsync();
    Task<OnboardingCompletionStepDto> GetCompletionStepAsync();
    Task<OnboardingPageDto> RehydrateIdentityPageAsync(OnboardingIdentityStepDto dto);
    Task<OnboardingPageDto> RehydrateDefaultsPageAsync(OnboardingDefaultsStepDto dto);
    Task SaveIdentityAsync(OnboardingIdentityStepDto dto);
    Task SaveDefaultsAsync(OnboardingDefaultsStepDto dto);
    Task CompleteAsync();
    Task SkipAsync();
    Task<bool> ShouldRedirectToOnboardingAsync();
}

public class OnboardingService : IOnboardingService
{
    private static readonly SemaphoreSlim StateLock = new(1, 1);
    private static readonly SemaphoreSlim BrandingLock = new(1, 1);
    private readonly TenantDbContext _db;
    private readonly CoreDbContext _coreDb;
    private readonly ITenantContext _tenantContext;

    public OnboardingService(TenantDbContext db, CoreDbContext coreDb, ITenantContext tenantContext)
    {
        _db = db;
        _coreDb = coreDb;
        _tenantContext = tenantContext;
    }

    public async Task<OnboardingPageDto> GetPageAsync(int? requestedStep = null)
    {
        var state = await GetOrCreateStateAsync();
        return await BuildPageAsync(state, ResolveStep(state, requestedStep));
    }

    public async Task<OnboardingIdentityStepDto> GetIdentityStepAsync()
    {
        var branding = await GetOrCreateBrandingAsync();
        var tenant = await GetTenantAsync();
        return BuildIdentityStep(branding, tenant);
    }

    public async Task<OnboardingDefaultsStepDto> GetDefaultsStepAsync()
    {
        var state = await GetOrCreateStateAsync();
        var branding = await GetOrCreateBrandingAsync();
        return BuildDefaultsStep(state, branding);
    }

    public async Task<OnboardingCompletionStepDto> GetCompletionStepAsync()
    {
        var state = await GetOrCreateStateAsync();
        var branding = await GetOrCreateBrandingAsync();
        var tenant = await GetTenantAsync();
        return BuildCompletionStep(state, branding, tenant);
    }

    public async Task<OnboardingPageDto> RehydrateIdentityPageAsync(OnboardingIdentityStepDto dto)
    {
        var state = await GetOrCreateStateAsync();
        return await BuildPageAsync(state, 1, identityOverride: dto);
    }

    public async Task<OnboardingPageDto> RehydrateDefaultsPageAsync(OnboardingDefaultsStepDto dto)
    {
        var state = await GetOrCreateStateAsync();
        return await BuildPageAsync(state, 2, defaultsOverride: dto);
    }

    public async Task SaveIdentityAsync(OnboardingIdentityStepDto dto)
    {
        var state = await GetOrCreateStateAsync();
        var branding = await GetOrCreateBrandingAsync();

        branding.AgencyName = Normalize(dto.AgencyName);
        branding.PublicContactEmail = Normalize(dto.PublicContactEmail);
        branding.ContactPhone = Normalize(dto.ContactPhone);
        branding.Website = Normalize(dto.Website);
        branding.LogoUrl = Normalize(dto.LogoUrl);
        branding.PrimaryColor = dto.PrimaryColor.Trim().ToUpperInvariant();
        branding.SecondaryColor = dto.SecondaryColor.Trim().ToUpperInvariant();

        state.IdentityCompletedAt = DateTime.UtcNow;
        state.CurrentStep = Math.Max(state.CurrentStep, 2);

        await _db.SaveChangesAsync();
    }

    public async Task SaveDefaultsAsync(OnboardingDefaultsStepDto dto)
    {
        var state = await GetOrCreateStateAsync();
        var branding = await GetOrCreateBrandingAsync();

        branding.QuotePrefix = dto.QuotePrefix.Trim().ToUpperInvariant();
        branding.DefaultQuoteValidityDays = dto.DefaultQuoteValidityDays;
        branding.DefaultQuoteMarkupPercentage = dto.DefaultQuoteMarkupPercentage;
        branding.QuoteResetSequenceYearly = dto.QuoteResetSequenceYearly;

        state.PreferredWorkspace = NormalizeWorkspace(dto.PreferredWorkspace);
        state.DefaultsCompletedAt = DateTime.UtcNow;
        state.CurrentStep = 3;

        await _db.SaveChangesAsync();
    }

    public async Task CompleteAsync()
    {
        var state = await GetOrCreateStateAsync();
        if (!state.IdentityCompletedAt.HasValue || !state.DefaultsCompletedAt.HasValue)
        {
            throw new InvalidOperationException("Complete the earlier steps first.");
        }

        state.CompletedAt = DateTime.UtcNow;
        state.CurrentStep = 3;
        await _db.SaveChangesAsync();
    }

    public async Task SkipAsync()
    {
        var state = await GetOrCreateStateAsync();
        if (!state.CompletedAt.HasValue)
        {
            state.SkippedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ShouldRedirectToOnboardingAsync()
    {
        var state = await _db.Set<TenantOnboardingState>().SingleOrDefaultAsync();
        return state is null || (!state.CompletedAt.HasValue && !state.SkippedAt.HasValue);
    }

    private async Task<OnboardingPageDto> BuildPageAsync(
        TenantOnboardingState state,
        int step,
        OnboardingIdentityStepDto? identityOverride = null,
        OnboardingDefaultsStepDto? defaultsOverride = null)
    {
        var branding = await GetOrCreateBrandingAsync();
        var tenant = await GetTenantAsync();
        var resolvedStep = ResolveStep(state, step);

        return new OnboardingPageDto
        {
            TenantName = tenant?.Name ?? _tenantContext.TenantName ?? "Travel Workspace",
            CurrentStep = resolvedStep,
            IsCompleted = state.CompletedAt.HasValue,
            CanSkip = !state.CompletedAt.HasValue,
            Steps = BuildStepSummaries(state, resolvedStep),
            Preview = BuildPreview(state, branding, tenant, identityOverride, defaultsOverride),
            IdentityStep = identityOverride ?? BuildIdentityStep(branding, tenant),
            DefaultsStep = defaultsOverride ?? BuildDefaultsStep(state, branding),
            CompletionStep = BuildCompletionStep(state, branding, tenant)
        };
    }

    private static List<OnboardingStepSummaryDto> BuildStepSummaries(TenantOnboardingState state, int currentStep)
    {
        return
        [
            new()
            {
                Number = 1,
                Title = "Identity",
                Description = "Set the public brand clients will recognize.",
                IsCurrent = currentStep == 1,
                IsComplete = state.IdentityCompletedAt.HasValue || state.CompletedAt.HasValue,
                IsAvailable = true
            },
            new()
            {
                Number = 2,
                Title = "Defaults",
                Description = "Choose the quote rules your team starts from.",
                IsCurrent = currentStep == 2,
                IsComplete = state.DefaultsCompletedAt.HasValue || state.CompletedAt.HasValue,
                IsAvailable = state.IdentityCompletedAt.HasValue || state.DefaultsCompletedAt.HasValue || state.CompletedAt.HasValue
            },
            new()
            {
                Number = 3,
                Title = "Ready",
                Description = "Confirm setup and jump into the workspace.",
                IsCurrent = currentStep == 3,
                IsComplete = state.CompletedAt.HasValue,
                IsAvailable = state.DefaultsCompletedAt.HasValue || state.CompletedAt.HasValue
            }
        ];
    }

    private OnboardingPreviewDto BuildPreview(
        TenantOnboardingState state,
        BrandingSettings branding,
        saas.Modules.Tenancy.Entities.Tenant? tenant,
        OnboardingIdentityStepDto? identityOverride,
        OnboardingDefaultsStepDto? defaultsOverride)
    {
        var primaryColor = identityOverride?.PrimaryColor?.Trim().ToUpperInvariant() ?? branding.PrimaryColor;
        var secondaryColor = identityOverride?.SecondaryColor?.Trim().ToUpperInvariant() ?? branding.SecondaryColor;
        var effectiveAgencyName = Normalize(identityOverride?.AgencyName) ?? branding.AgencyName ?? tenant?.Name ?? _tenantContext.TenantName ?? "Travel Workspace";
        var effectiveContactEmail = Normalize(identityOverride?.PublicContactEmail) ?? branding.PublicContactEmail ?? tenant?.ContactEmail ?? string.Empty;
        var quotePrefix = defaultsOverride?.QuotePrefix?.Trim().ToUpperInvariant() ?? branding.QuotePrefix;
        var preferredWorkspace = NormalizeWorkspace(defaultsOverride?.PreferredWorkspace ?? state.PreferredWorkspace);
        var previewReference = QuoteReferenceFormatHelper.Format(branding.QuoteNumberFormat, quotePrefix, GetEffectiveSequence(branding), DateTime.UtcNow);

        return new OnboardingPreviewDto
        {
            EffectiveAgencyName = effectiveAgencyName,
            EffectiveContactEmail = effectiveContactEmail,
            LogoUrl = Normalize(identityOverride?.LogoUrl) ?? branding.LogoUrl,
            PrimaryColor = primaryColor,
            SecondaryColor = secondaryColor,
            PrimaryTextColor = GetReadableTextColor(primaryColor),
            SecondaryTextColor = GetReadableTextColor(secondaryColor),
            PreviewReferenceNumber = previewReference,
            PreferredWorkspace = preferredWorkspace,
            PreferredWorkspaceLabel = GetWorkspaceLabel(preferredWorkspace),
            NextActionUrl = BuildWorkspaceUrl(preferredWorkspace),
            NextActionLabel = $"Open {GetWorkspaceLabel(preferredWorkspace)}"
        };
    }

    private static OnboardingIdentityStepDto BuildIdentityStep(BrandingSettings branding, saas.Modules.Tenancy.Entities.Tenant? tenant)
    {
        return new OnboardingIdentityStepDto
        {
            AgencyName = branding.AgencyName,
            PublicContactEmail = branding.PublicContactEmail ?? tenant?.ContactEmail,
            ContactPhone = branding.ContactPhone,
            Website = branding.Website,
            LogoUrl = branding.LogoUrl,
            PrimaryColor = branding.PrimaryColor,
            SecondaryColor = branding.SecondaryColor
        };
    }

    private static OnboardingDefaultsStepDto BuildDefaultsStep(TenantOnboardingState state, BrandingSettings branding)
    {
        return new OnboardingDefaultsStepDto
        {
            QuotePrefix = branding.QuotePrefix,
            DefaultQuoteValidityDays = branding.DefaultQuoteValidityDays,
            DefaultQuoteMarkupPercentage = branding.DefaultQuoteMarkupPercentage,
            QuoteResetSequenceYearly = branding.QuoteResetSequenceYearly,
            PreferredWorkspace = NormalizeWorkspace(state.PreferredWorkspace)
        };
    }

    private OnboardingCompletionStepDto BuildCompletionStep(
        TenantOnboardingState state,
        BrandingSettings branding,
        saas.Modules.Tenancy.Entities.Tenant? tenant)
    {
        var preferredWorkspace = NormalizeWorkspace(state.PreferredWorkspace);
        var displayName = branding.AgencyName ?? tenant?.Name ?? _tenantContext.TenantName ?? "Travel Workspace";

        return new OnboardingCompletionStepDto
        {
            IsCompleted = state.CompletedAt.HasValue,
            Headline = state.CompletedAt.HasValue ? "Workspace ready." : "Review your launch settings.",
            Summary = state.CompletedAt.HasValue
                ? "Your team can head straight into the chosen workspace or return to the dashboard at any time."
                : "You have saved your public identity and quote defaults. Confirm the setup to unlock the normal dashboard flow.",
            NextActionUrl = BuildWorkspaceUrl(preferredWorkspace),
            NextActionLabel = $"Open {GetWorkspaceLabel(preferredWorkspace)}",
            Highlights =
            [
                $"Brand: {displayName}",
                $"Quote prefix: {branding.QuotePrefix}",
                $"Preferred workspace: {GetWorkspaceLabel(preferredWorkspace)}"
            ]
        };
    }

    private int ResolveStep(TenantOnboardingState state, int? requestedStep)
    {
        if (state.CompletedAt.HasValue)
        {
            return 3;
        }

        var highestAvailable = 1;
        if (state.IdentityCompletedAt.HasValue)
        {
            highestAvailable = 2;
        }

        if (state.DefaultsCompletedAt.HasValue)
        {
            highestAvailable = 3;
        }

        var target = requestedStep ?? state.CurrentStep;
        target = Math.Clamp(target, 1, 3);
        return Math.Min(target, highestAvailable);
    }

    private async Task<TenantOnboardingState> GetOrCreateStateAsync()
    {
        var state = await _db.Set<TenantOnboardingState>().SingleOrDefaultAsync();
        if (state is not null)
        {
            return state;
        }

        await StateLock.WaitAsync();
        try
        {
            state = await _db.Set<TenantOnboardingState>().SingleOrDefaultAsync();
            if (state is not null)
            {
                return state;
            }

            state = new TenantOnboardingState();
            _db.Set<TenantOnboardingState>().Add(state);
            await _db.SaveChangesAsync();
            return state;
        }
        finally
        {
            StateLock.Release();
        }
    }

    private async Task<BrandingSettings> GetOrCreateBrandingAsync()
    {
        var branding = await _db.Set<BrandingSettings>().SingleOrDefaultAsync();
        if (branding is not null)
        {
            return branding;
        }

        await BrandingLock.WaitAsync();
        try
        {
            branding = await _db.Set<BrandingSettings>().SingleOrDefaultAsync();
            if (branding is not null)
            {
                return branding;
            }

            branding = new BrandingSettings();
            _db.Set<BrandingSettings>().Add(branding);
            await _db.SaveChangesAsync();
            return branding;
        }
        finally
        {
            BrandingLock.Release();
        }
    }

    private Task<saas.Modules.Tenancy.Entities.Tenant?> GetTenantAsync()
        => _coreDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _tenantContext.TenantId);

    private string BuildWorkspaceUrl(string preferredWorkspace)
        => preferredWorkspace switch
        {
            OnboardingWorkspaceOptions.RateCards => $"/{_tenantContext.Slug}/rate-cards",
            OnboardingWorkspaceOptions.Bookings => $"/{_tenantContext.Slug}/bookings",
            _ => $"/{_tenantContext.Slug}/quotes/new"
        };

    private static string GetWorkspaceLabel(string preferredWorkspace)
        => preferredWorkspace switch
        {
            OnboardingWorkspaceOptions.RateCards => "Rate Cards",
            OnboardingWorkspaceOptions.Bookings => "Bookings",
            _ => "Quotes"
        };

    private static string NormalizeWorkspace(string? preferredWorkspace)
        => preferredWorkspace switch
        {
            OnboardingWorkspaceOptions.RateCards => OnboardingWorkspaceOptions.RateCards,
            OnboardingWorkspaceOptions.Bookings => OnboardingWorkspaceOptions.Bookings,
            _ => OnboardingWorkspaceOptions.Quotes
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int GetEffectiveSequence(BrandingSettings settings)
    {
        if (!settings.QuoteResetSequenceYearly)
        {
            return settings.NextQuoteSequence;
        }

        var currentYear = DateTime.UtcNow.Year;
        return settings.QuoteSequenceLastResetYear == currentYear ? settings.NextQuoteSequence : 1;
    }

    private static string GetReadableTextColor(string hexColor)
    {
        var (r, g, b) = ParseHex(hexColor);
        var luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
        return luminance > 160 ? "#0F172A" : "#FFFFFF";
    }

    private static (int R, int G, int B) ParseHex(string hexColor)
    {
        var value = hexColor.TrimStart('#');
        return (Convert.ToInt32(value[..2], 16), Convert.ToInt32(value[2..4], 16), Convert.ToInt32(value[4..6], 16));
    }
}
