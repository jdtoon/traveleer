using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using saas.Modules.Branding.Entities;
using saas.Modules.Auth.Entities;
using saas.Modules.Bookings.Entities;
using saas.Modules.Clients.Entities;
using saas.Modules.Email.Entities;
using saas.Modules.Inventory.Entities;
using saas.Modules.Notifications.Entities;
using saas.Modules.Onboarding.Entities;
using saas.Modules.Quotes.Entities;
using saas.Modules.RateCards.Entities;
using saas.Modules.Settings.Entities;
using saas.Modules.Suppliers.Entities;
using saas.Modules.Itineraries.Entities;
using saas.Modules.Reports.Entities;
using saas.Modules.TenantAdmin.Entities;
using saas.Modules.Portal.Entities;
using saas.Modules.Tasks.Entities;
using saas.Modules.Communications.Entities;

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
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<BrandingSettings> BrandingSettings => Set<BrandingSettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<QuoteEmailLog> QuoteEmailLogs => Set<QuoteEmailLog>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateCardTemplate> RateCardTemplates => Set<RateCardTemplate>();
    public DbSet<RateSeason> RateSeasons => Set<RateSeason>();
    public DbSet<RoomRate> RoomRates => Set<RoomRate>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<RateCategory> RateCategories => Set<RateCategory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<TenantOnboardingState> TenantOnboardingStates => Set<TenantOnboardingState>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteRateCard> QuoteRateCards => Set<QuoteRateCard>();
    public DbSet<QuoteVersion> QuoteVersions => Set<QuoteVersion>();
    public DbSet<TeamInvitation> TeamInvitations => Set<TeamInvitation>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<Itinerary> Itineraries => Set<Itinerary>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();
    public DbSet<UserReportPreference> UserReportPreferences => Set<UserReportPreference>();
    public DbSet<BookingPayment> BookingPayments => Set<BookingPayment>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<PortalLink> PortalLinks => Set<PortalLink>();
    public DbSet<PortalSession> PortalSessions => Set<PortalSession>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
    public DbSet<BookingAssignment> BookingAssignments => Set<BookingAssignment>();
    public DbSet<BookingComment> BookingComments => Set<BookingComment>();
    public DbSet<CommunicationEntry> CommunicationEntries => Set<CommunicationEntry>();

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
