using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using saas.Data.Core;
using saas.Modules.SuperAdmin.Entities;

namespace saas.Modules.SuperAdmin.Services;

public class AnnouncementService : IAnnouncementService
{
    private readonly CoreDbContext _coreDb;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "active_announcements";

    public AnnouncementService(CoreDbContext coreDb, IMemoryCache cache)
    {
        _coreDb = coreDb;
        _cache = cache;
    }

    public async Task<List<Announcement>> GetActiveAnnouncementsAsync()
    {
        return await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            var now = DateTime.UtcNow;
            return await _coreDb.Announcements
                .AsNoTracking()
                .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > now))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();
        }) ?? [];
    }

    public async Task<List<Announcement>> GetAllAnnouncementsAsync()
    {
        return await _coreDb.Announcements
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task DeactivateAsync(Guid id)
    {
        var announcement = await _coreDb.Announcements.FindAsync(id);
        if (announcement is not null)
        {
            announcement.IsActive = false;
            await _coreDb.SaveChangesAsync();
            _cache.Remove(CacheKey);
        }
    }
}
