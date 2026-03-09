using saas.Modules.TenantAdmin;
using saas.Shared;
using Xunit;

namespace saas.Tests.Shared;

public class ModulePermissionsTests
{
    [Fact]
    public void TenantAdminPermissions_AllUniqueKeys()
    {
        var module = new TenantAdminModule();
        var permissions = module.Permissions;

        Assert.NotEmpty(permissions);
        var distinct = permissions.Select(p => p.Key).Distinct().Count();
        Assert.Equal(permissions.Count, distinct);
    }

    [Fact]
    public void AllModules_HaveUniquePermissionKeys()
    {
        IModule[] modules =
        [
            new TenantAdminModule(),
            new saas.Modules.Audit.AuditModule(),
        ];

        var allKeys = modules.SelectMany(m => m.Permissions).Select(p => p.Key).ToList();
        var distinct = allKeys.Distinct().Count();
        Assert.Equal(allKeys.Count, distinct);
    }
}
