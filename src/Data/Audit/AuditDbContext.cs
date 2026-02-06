using Microsoft.EntityFrameworkCore;

namespace saas.Data.Audit;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AuditDbContext).Assembly,
            t => t.Namespace?.Contains("Data.Audit") == true
        );
    }
}
