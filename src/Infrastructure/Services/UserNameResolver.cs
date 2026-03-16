using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using saas.Data.Tenant;
using saas.Shared;

namespace saas.Infrastructure.Services;

public class UserNameResolver : IUserNameResolver
{
    private readonly TenantDbContext _db;
    private readonly Dictionary<string, string> _names = new();

    public UserNameResolver(TenantDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, string>> ResolveNamesAsync(IEnumerable<string> userIds)
    {
        var missingIds = userIds.Distinct().Where(id => !_names.ContainsKey(id)).ToList();

        if (missingIds.Any())
        {
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => missingIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName, u.Email })
                .ToListAsync();

            foreach (var u in users)
            {
                _names[u.Id] = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName 
                    : (!string.IsNullOrWhiteSpace(u.Email) ? u.Email : u.Id);
            }

            // Fill missing ones with ID to avoid querying again
            foreach (var id in missingIds)
            {
                if (!_names.ContainsKey(id))
                {
                    _names[id] = id;
                }
            }
        }

        var result = new Dictionary<string, string>();
        foreach (var id in userIds.Distinct())
        {
            if (_names.TryGetValue(id, out var name))
            {
                result[id] = name;
            }
            else
            {
                result[id] = id;
            }
        }

        return result;
    }
}
