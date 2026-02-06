using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace saas.Data.Tenant;

public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants");
        Directory.CreateDirectory(basePath);

        var connectionString = $"Data Source={Path.Combine(basePath, "_design-time.db")}";
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new TenantDbContext(options);
    }
}