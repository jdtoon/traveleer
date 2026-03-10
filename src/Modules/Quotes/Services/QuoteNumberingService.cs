using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Modules.Branding.Entities;
using saas.Modules.Branding.Services;

namespace saas.Modules.Quotes.Services;

public interface IQuoteNumberingService
{
    Task<string> PreviewNextReferenceAsync();
    Task<string> GenerateNextReferenceAsync();
}

public class QuoteNumberingService : IQuoteNumberingService
{
    private readonly TenantDbContext _db;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public QuoteNumberingService(TenantDbContext db)
    {
        _db = db;
    }

    public Task<string> PreviewNextReferenceAsync()
        => GetNextReferenceAsync(reserve: false);

    public Task<string> GenerateNextReferenceAsync()
        => GetNextReferenceAsync(reserve: true);

    private async Task<string> GetNextReferenceAsync(bool reserve)
    {
        if (reserve)
        {
            await Lock.WaitAsync();
        }

        try
        {
            var settings = await GetOrCreateBrandingAsync();
            var now = DateTime.UtcNow;
            var sequence = settings.NextQuoteSequence;

            if (settings.QuoteResetSequenceYearly && settings.QuoteSequenceLastResetYear != now.Year)
            {
                sequence = 1;
            }

            var reference = QuoteReferenceFormatHelper.Format(settings.QuoteNumberFormat, settings.QuotePrefix, sequence, now);
            while (await _db.Quotes.AsNoTracking().AnyAsync(x => x.ReferenceNumber == reference))
            {
                sequence += 1;
                reference = QuoteReferenceFormatHelper.Format(settings.QuoteNumberFormat, settings.QuotePrefix, sequence, now);
            }

            if (reserve)
            {
                settings.NextQuoteSequence = sequence + 1;
                settings.QuoteSequenceLastResetYear = now.Year;
                await _db.SaveChangesAsync();
            }

            return reference;
        }
        finally
        {
            if (reserve)
            {
                Lock.Release();
            }
        }
    }

    private async Task<BrandingSettings> GetOrCreateBrandingAsync()
    {
        var settings = await _db.BrandingSettings.FirstOrDefaultAsync();
        if (settings is not null)
        {
            return settings;
        }

        settings = new BrandingSettings();
        _db.BrandingSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }
}
