using saas.Shared;
using Xunit;

namespace saas.Tests.Shared;

public class PermissionDefinitionsTests
{
    [Fact]
    public void GetAll_ReturnsNonEmpty_UniqueKeys()
    {
        var permissions = PermissionDefinitions.GetAll();

        Assert.NotEmpty(permissions);
        var distinct = permissions.Select(p => p.Key).Distinct().Count();
        Assert.Equal(permissions.Count, distinct);
    }
}
