using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;

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
            var year = DateTime.UtcNow.Year;
            var prefix = $"QT-{year}-";
            var references = await _db.Quotes
                .AsNoTracking()
                .Where(x => x.ReferenceNumber.StartsWith(prefix))
                .Select(x => x.ReferenceNumber)
                .ToListAsync();

            var next = references
                .Select(ParseSequence)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"QT-{year}-{next:D4}";
        }
        finally
        {
            if (reserve)
            {
                Lock.Release();
            }
        }
    }

    private static int ParseSequence(string referenceNumber)
    {
        var parts = referenceNumber.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && int.TryParse(parts[^1], out var parsed) ? parsed : 0;
    }
}
