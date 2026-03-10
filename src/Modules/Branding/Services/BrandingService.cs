using Microsoft.EntityFrameworkCore;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Modules.Branding.DTOs;
using saas.Modules.Branding.Entities;
using saas.Shared;

namespace saas.Modules.Branding.Services;

public interface IBrandingService
{
    Task<BrandingSettingsDto> GetSettingsAsync();
    Task UpdateAsync(BrandingSettingsDto dto);
    Task<BrandingShellDto> GetShellAsync();
    Task<BrandingThemeDto> GetThemeAsync();
    Task<QuoteBrandingDto> GetQuoteBrandingAsync();
}

public class BrandingService : IBrandingService
{
    private static readonly SemaphoreSlim SettingsLock = new(1, 1);
    private readonly TenantDbContext _db;
    private readonly CoreDbContext _coreDb;
    private readonly ITenantContext _tenantContext;

    public BrandingService(TenantDbContext db, CoreDbContext coreDb, ITenantContext tenantContext)
    {
        _db = db;
        _coreDb = coreDb;
        _tenantContext = tenantContext;
    }

    public async Task<BrandingSettingsDto> GetSettingsAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        var tenant = await GetTenantAsync();
        var previewNumber = QuoteReferenceFormatHelper.Format(settings.QuoteNumberFormat, settings.QuotePrefix, GetEffectiveSequence(settings), DateTime.UtcNow);

        return new BrandingSettingsDto
        {
            AgencyName = settings.AgencyName,
            PublicContactEmail = settings.PublicContactEmail ?? tenant?.ContactEmail,
            ContactPhone = settings.ContactPhone,
            Website = settings.Website,
            Address = settings.Address,
            LogoUrl = settings.LogoUrl,
            PrimaryColor = settings.PrimaryColor,
            SecondaryColor = settings.SecondaryColor,
            QuotePrefix = settings.QuotePrefix,
            QuoteNumberFormat = settings.QuoteNumberFormat,
            NextQuoteSequence = settings.NextQuoteSequence,
            QuoteResetSequenceYearly = settings.QuoteResetSequenceYearly,
            DefaultQuoteValidityDays = settings.DefaultQuoteValidityDays,
            DefaultQuoteMarkupPercentage = settings.DefaultQuoteMarkupPercentage,
            PdfFooterText = settings.PdfFooterText,
            PreviewReferenceNumber = previewNumber,
            EffectiveAgencyName = settings.AgencyName ?? tenant?.Name ?? "Travel Workspace",
            EffectiveContactEmail = settings.PublicContactEmail ?? tenant?.ContactEmail ?? string.Empty,
            PrimaryTextColor = GetReadableTextColor(settings.PrimaryColor),
            SecondaryTextColor = GetReadableTextColor(settings.SecondaryColor)
        };
    }

    public async Task UpdateAsync(BrandingSettingsDto dto)
    {
        var settings = await GetOrCreateSettingsAsync();
        settings.AgencyName = Normalize(dto.AgencyName);
        settings.PublicContactEmail = Normalize(dto.PublicContactEmail);
        settings.ContactPhone = Normalize(dto.ContactPhone);
        settings.Website = Normalize(dto.Website);
        settings.Address = Normalize(dto.Address);
        settings.LogoUrl = Normalize(dto.LogoUrl);
        settings.PrimaryColor = dto.PrimaryColor.Trim().ToUpperInvariant();
        settings.SecondaryColor = dto.SecondaryColor.Trim().ToUpperInvariant();
        settings.QuotePrefix = dto.QuotePrefix.Trim().ToUpperInvariant();
        settings.QuoteNumberFormat = dto.QuoteNumberFormat.Trim();
        settings.NextQuoteSequence = Math.Max(1, dto.NextQuoteSequence);
        settings.QuoteResetSequenceYearly = dto.QuoteResetSequenceYearly;
        settings.DefaultQuoteValidityDays = dto.DefaultQuoteValidityDays;
        settings.DefaultQuoteMarkupPercentage = dto.DefaultQuoteMarkupPercentage;
        settings.PdfFooterText = Normalize(dto.PdfFooterText);

        await _db.SaveChangesAsync();
    }

    public async Task<BrandingShellDto> GetShellAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        var tenant = await GetTenantAsync();
        return new BrandingShellDto
        {
            DisplayName = settings.AgencyName ?? tenant?.Name ?? _tenantContext.TenantName ?? "Travel Workspace",
            LogoUrl = settings.LogoUrl,
            AccentColor = settings.PrimaryColor
        };
    }

    public async Task<BrandingThemeDto> GetThemeAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        return new BrandingThemeDto
        {
            PrimaryColor = settings.PrimaryColor,
            SecondaryColor = settings.SecondaryColor,
            PrimaryTextColor = GetReadableTextColor(settings.PrimaryColor),
            SecondaryTextColor = GetReadableTextColor(settings.SecondaryColor),
            PrimarySoftColor = MixWithWhite(settings.PrimaryColor, 0.84),
            SecondarySoftColor = MixWithWhite(settings.SecondaryColor, 0.88),
            ThemeColor = settings.PrimaryColor
        };
    }

    public async Task<QuoteBrandingDto> GetQuoteBrandingAsync()
    {
        var settings = await GetOrCreateSettingsAsync();
        var tenant = await GetTenantAsync();
        return new QuoteBrandingDto
        {
            AgencyName = settings.AgencyName ?? tenant?.Name ?? _tenantContext.TenantName ?? "Travel Workspace",
            ContactEmail = settings.PublicContactEmail ?? tenant?.ContactEmail,
            ContactPhone = settings.ContactPhone,
            Website = settings.Website,
            LogoUrl = settings.LogoUrl,
            FooterText = settings.PdfFooterText
        };
    }

    private async Task<BrandingSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.Set<BrandingSettings>().FirstOrDefaultAsync();
        if (settings is not null)
        {
            return settings;
        }

        await SettingsLock.WaitAsync();
        try
        {
            settings = await _db.Set<BrandingSettings>().FirstOrDefaultAsync();
            if (settings is not null)
            {
                return settings;
            }

            settings = new BrandingSettings();
            _db.Set<BrandingSettings>().Add(settings);
            await _db.SaveChangesAsync();
            return settings;
        }
        finally
        {
            SettingsLock.Release();
        }
    }

    private Task<saas.Modules.Tenancy.Entities.Tenant?> GetTenantAsync()
        => _coreDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _tenantContext.TenantId);

    private static int GetEffectiveSequence(BrandingSettings settings)
    {
        if (!settings.QuoteResetSequenceYearly)
        {
            return settings.NextQuoteSequence;
        }

        var currentYear = DateTime.UtcNow.Year;
        return settings.QuoteSequenceLastResetYear == currentYear ? settings.NextQuoteSequence : 1;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetReadableTextColor(string hexColor)
    {
        var (r, g, b) = ParseHex(hexColor);
        var luminance = (0.299 * r) + (0.587 * g) + (0.114 * b);
        return luminance > 160 ? "#0F172A" : "#FFFFFF";
    }

    private static string MixWithWhite(string hexColor, double whiteRatio)
    {
        var (r, g, b) = ParseHex(hexColor);
        var mixedR = (int)Math.Round((r * (1 - whiteRatio)) + (255 * whiteRatio));
        var mixedG = (int)Math.Round((g * (1 - whiteRatio)) + (255 * whiteRatio));
        var mixedB = (int)Math.Round((b * (1 - whiteRatio)) + (255 * whiteRatio));
        return $"#{mixedR:X2}{mixedG:X2}{mixedB:X2}";
    }

    private static (int R, int G, int B) ParseHex(string hexColor)
    {
        var value = hexColor.TrimStart('#');
        return (Convert.ToInt32(value[..2], 16), Convert.ToInt32(value[2..4], 16), Convert.ToInt32(value[4..6], 16));
    }
}

internal static class QuoteReferenceFormatHelper
{
    public static string Format(string format, string prefix, int sequence, DateTime timestamp)
    {
        var result = format
            .Replace("{PREFIX}", prefix, StringComparison.OrdinalIgnoreCase)
            .Replace("{YEAR}", timestamp.Year.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{YEAR2}", (timestamp.Year % 100).ToString("D2"), StringComparison.OrdinalIgnoreCase)
            .Replace("{MONTH}", timestamp.Month.ToString("D2"), StringComparison.OrdinalIgnoreCase);

        var match = System.Text.RegularExpressions.Regex.Match(result, "\\{SEQ:(\\d+)\\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var padding = int.Parse(match.Groups[1].Value);
            result = System.Text.RegularExpressions.Regex.Replace(result, "\\{SEQ:\\d+\\}", sequence.ToString().PadLeft(padding, '0'), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        else
        {
            result = result.Replace("{SEQ}", sequence.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
