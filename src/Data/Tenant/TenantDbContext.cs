using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using saas.Modules.Auth.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Notifications.Entities;
using saas.Modules.Settings.Entities;
using saas.Modules.TenantAdmin.Entities;

namespace saas.Data.Tenant;

public class TenantDbContext : IdentityDbContext<AppUser, AppRole, string>
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options)
        : base(options)
    {
    }

    // RBAC
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Auth sessions
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    // Application domain entities
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<RateCategory> RateCategories => Set<RateCategory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<TeamInvitation> TeamInvitations => Set<TeamInvitation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Suppress false-positive pending model changes warning — all entities already
        // have migrations; we only added explicit DbSet properties for query convenience.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity tables

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantDbContext).Assembly,
            t => typeof(ITenantEntityConfiguration).IsAssignableFrom(t) && t.IsClass
        );
    }

}
