using System.Collections.Generic;
using System.Threading.Tasks;

namespace saas.Shared;

public interface IUserNameResolver
{
    Task<Dictionary<string, string>> ResolveNamesAsync(IEnumerable<string> userIds);
}
