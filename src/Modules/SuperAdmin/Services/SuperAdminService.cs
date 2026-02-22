using Microsoft.EntityFrameworkCore;
using saas.Data;
using saas.Data.Core;
using saas.Data.Tenant;
using saas.Shared;

namespace saas.Modules.SuperAdmin.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly CoreDbContext _coreDb;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBillingService _billingService;
    private readonly ILitestreamStatusService _litestreamStatusService;

    public SuperAdminService(
        CoreDbContext coreDb,
        IServiceProvider serviceProvider,
        IBillingService billingService,
        ILitestreamStatusService litestreamStatusService)
    {
        _coreDb = coreDb;
        _serviceProvider = serviceProvider;
        _billingService = billingService;
        _litestreamStatusService = litestreamStatusService;
    }

    // ── Dashboard ────────────────────────────────────────────────────────────

    public async Task<SuperAdminDashboardModel> GetDashboardAsync()
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var tenantCount = await _coreDb.Tenants.CountAsync();
        var activeSubscriptions = await _coreDb.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Active);
        var recentRegistrations = await _coreDb.Tenants
            .CountAsync(t => t.CreatedAt >= thirtyDaysAgo);
        var recentTenants = await _coreDb.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => new TenantListItem
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                ContactEmail = t.ContactEmail,
                Status = t.Status,
                PlanName = t.Plan.Name,
                SubscriptionStatus = t.ActiveSubscription != null ? t.ActiveSubscription.Status : null,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return new SuperAdminDashboardModel
        {
            TenantCount = tenantCount,
            ActiveSubscriptions = activeSubscriptions,
            RecentRegistrations = recentRegistrations,
            RecentTenants = recentTenants
        };
    }

    // ── Tenant Management ────────────────────────────────────────────────────

    public async Task<PaginatedList<TenantListItem>> GetTenantsAsync(string? search = null, int page = 1, int pageSize = 20)
    {
        var query = _coreDb.Tenants.Include(t => t.Plan).Include(t => t.ActiveSubscription).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Slug.ToLower().Contains(term) ||
                t.ContactEmail.ToLower().Contains(term));
        }

        var projected = query
            .OrderBy(t => t.Name)
            .Select(t => new TenantListItem
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                ContactEmail = t.ContactEmail,
                Status = t.Status,
                PlanName = t.Plan.Name,
                SubscriptionStatus = t.ActiveSubscription != null ? t.ActiveSubscription.Status : null,
                CreatedAt = t.CreatedAt
            });

        return await PaginatedList<TenantListItem>.CreateAsync(projected, page, pageSize);
    }

    public async Task<TenantDetailModel?> GetTenantDetailAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return null;

        var userCount = await GetTenantUserCountAsync(tenant.Slug);

        // Load all features with plan-enabled status and per-tenant overrides
        var allFeatures = await _coreDb.Features
            .OrderBy(f => f.Module).ThenBy(f => f.Name)
            .ToListAsync();

        var planFeatureIds = await _coreDb.PlanFeatures
            .Where(pf => pf.PlanId == tenant.PlanId)
            .Select(pf => pf.FeatureId)
            .ToListAsync();
        var planFeatureSet = planFeatureIds.ToHashSet();

        var overrides = await _coreDb.TenantFeatureOverrides
            .Where(o => o.TenantId == tenant.Id)
            .ToListAsync();
        var overrideMap = overrides.ToDictionary(o => o.FeatureId);

        var featureItems = allFeatures.Select(f =>
        {
            overrideMap.TryGetValue(f.Id, out var ovr);
            return new TenantFeatureItem
            {
                FeatureId = f.Id,
                Name = f.Name,
                Module = f.Module,
                IsGlobal = f.IsGlobal,
                EnabledByPlan = planFeatureSet.Contains(f.Id),
                OverrideEnabled = ovr?.IsEnabled,
                OverrideReason = ovr?.Reason
            };
        }).ToList();

        return new TenantDetailModel
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            ContactEmail = tenant.ContactEmail,
            Status = tenant.Status,
            PlanName = tenant.Plan.Name,
            PlanId = tenant.PlanId,
            CreatedAt = tenant.CreatedAt,
            SubscriptionStatus = tenant.ActiveSubscription?.Status,
            NextBillingDate = tenant.ActiveSubscription?.NextBillingDate,
            UserCount = userCount,
            Features = featureItems
        };
    }

    public async Task<bool> SuspendTenantAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is null) return false;

        tenant.Status = TenantStatus.Suspended;
        await _coreDb.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ActivateTenantAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is null) return false;

        tenant.Status = TenantStatus.Active;
        await _coreDb.SaveChangesAsync();
        return true;
    }

    public async Task<(bool Success, string? OldPlanName, string? NewPlanName)> ChangeTenantPlanAsync(Guid tenantId, Guid newPlanId)
    {
        var tenant = await _coreDb.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return (false, null, null);

        var newPlan = await _coreDb.Plans.FindAsync(newPlanId);
        if (newPlan is null) return (false, null, null);

        var oldPlanName = tenant.Plan.Name;
        tenant.PlanId = newPlanId;
        await _coreDb.SaveChangesAsync();
        return (true, oldPlanName, newPlan.Name);
    }

    // ── Plan Management ──────────────────────────────────────────────────────

    public async Task<List<Plan>> GetPlansAsync()
    {
        return await _coreDb.Plans
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    public async Task<Plan?> GetPlanAsync(Guid planId)
    {
        return await _coreDb.Plans.FindAsync(planId);
    }

    public async Task SavePlanAsync(PlanEditModel model)
    {
        Plan plan;
        if (model.Id.HasValue && model.Id.Value != Guid.Empty)
        {
            plan = await _coreDb.Plans.FindAsync(model.Id.Value)
                ?? throw new InvalidOperationException("Plan not found");
        }
        else
        {
            plan = new Plan { Id = Guid.NewGuid() };
            _coreDb.Plans.Add(plan);
        }

        plan.Name = model.Name;
        plan.Slug = model.Slug;
        plan.Description = model.Description;
        plan.MonthlyPrice = model.MonthlyPrice;
        plan.AnnualPrice = model.AnnualPrice;
        plan.MaxUsers = model.MaxUsers;
        plan.MaxRequestsPerMinute = model.MaxRequestsPerMinute;
        plan.SortOrder = model.SortOrder;
        plan.IsActive = model.IsActive;

        await _coreDb.SaveChangesAsync();

        // Push price/name changes to the payment gateway (Paystack)
        // This will create the plan on Paystack if it doesn't exist yet, or update it
        if (plan.MonthlyPrice > 0)
        {
            await _billingService.UpdatePlanInGatewayAsync(plan.Id);
        }
    }

    public async Task<bool> IsSlugTakenAsync(string slug, Guid? excludeId = null)
    {
        var query = _coreDb.Plans.Where(p => p.Slug == slug);
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    // ── Feature Management ───────────────────────────────────────────────────

    public async Task<FeatureMatrixModel> GetFeatureMatrixAsync()
    {
        var plans = await _coreDb.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var features = await _coreDb.Features
            .OrderBy(f => f.Module).ThenBy(f => f.Name)
            .ToListAsync();

        var planFeatures = await _coreDb.PlanFeatures.ToListAsync();

        var enabled = new HashSet<string>(
            planFeatures.Select(pf => $"{pf.PlanId}:{pf.FeatureId}"));

        return new FeatureMatrixModel
        {
            Plans = plans,
            Features = features,
            EnabledCombinations = enabled
        };
    }

    public async Task TogglePlanFeatureAsync(Guid planId, Guid featureId)
    {
        var existing = await _coreDb.PlanFeatures
            .FirstOrDefaultAsync(pf => pf.PlanId == planId && pf.FeatureId == featureId);

        if (existing is not null)
        {
            _coreDb.PlanFeatures.Remove(existing);
        }
        else
        {
            _coreDb.PlanFeatures.Add(new PlanFeature
            {
                PlanId = planId,
                FeatureId = featureId
            });
        }

        await _coreDb.SaveChangesAsync();
    }

    public async Task<TenantFeatureOverrideModel?> GetTenantFeatureOverrideAsync(Guid tenantId, Guid featureId)
    {
        var ovr = await _coreDb.TenantFeatureOverrides
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.FeatureId == featureId);

        if (ovr is null) return null;

        return new TenantFeatureOverrideModel
        {
            TenantId = ovr.TenantId,
            FeatureId = ovr.FeatureId,
            IsEnabled = ovr.IsEnabled,
            Reason = ovr.Reason
        };
    }

    public async Task SaveTenantFeatureOverrideAsync(TenantFeatureOverrideModel model)
    {
        var existing = await _coreDb.TenantFeatureOverrides
            .FirstOrDefaultAsync(o => o.TenantId == model.TenantId && o.FeatureId == model.FeatureId);

        if (existing is not null)
        {
            existing.IsEnabled = model.IsEnabled;
            existing.Reason = model.Reason;
        }
        else
        {
            _coreDb.TenantFeatureOverrides.Add(new TenantFeatureOverride
            {
                Id = Guid.NewGuid(),
                TenantId = model.TenantId,
                FeatureId = model.FeatureId,
                IsEnabled = model.IsEnabled,
                Reason = model.Reason
            });
        }

        await _coreDb.SaveChangesAsync();
    }

    public Task<LitestreamStatusModel> GetLitestreamStatusAsync()
    {
        return _litestreamStatusService.GetStatusAsync();
    }

    public Task<List<DatabaseReplicationInfo>> GetDatabaseReplicationInfoAsync()
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "db");
        var tenantPath = Path.Combine(dbPath, "tenants");
        var results = new List<DatabaseReplicationInfo>();

        // Core databases
        AddDbInfo(results, dbPath, "core.db", "Core");
        AddDbInfo(results, dbPath, "audit.db", "Audit");
        AddDbInfo(results, dbPath, "hangfire.db", "Hangfire");

        // Tenant databases
        if (Directory.Exists(tenantPath))
        {
            foreach (var file in Directory.GetFiles(tenantPath, "*.db").OrderBy(f => f))
            {
                var fileName = Path.GetFileName(file);
                AddDbInfo(results, tenantPath, fileName, "Tenant");
            }
        }

        return Task.FromResult(results);
    }

    private static void AddDbInfo(List<DatabaseReplicationInfo> list, string dir, string fileName, string category)
    {
        var fullPath = Path.Combine(dir, fileName);
        if (!File.Exists(fullPath)) return;

        var fi = new FileInfo(fullPath);
        list.Add(new DatabaseReplicationInfo
        {
            FileName = fileName,
            Category = category,
            SizeBytes = fi.Length,
            LastModifiedUtc = fi.LastWriteTimeUtc
        });
    }

    // ── Tenant Health (Item 14) ──────────────────────────────────────────────

    public async Task<TenantHealthOverviewModel> GetTenantHealthOverviewAsync()
    {
        var tenants = await _coreDb.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync();

        var model = new TenantHealthOverviewModel
        {
            TotalTenants = tenants.Count,
            ActiveTenants = tenants.Count(t => t.Status == TenantStatus.Active),
            SuspendedTenants = tenants.Count(t => t.Status == TenantStatus.Suspended),
            TrialingTenants = tenants.Count(t => t.ActiveSubscription != null && t.ActiveSubscription.TrialEndsAt.HasValue && t.ActiveSubscription.TrialEndsAt > DateTime.UtcNow)
        };

        foreach (var t in tenants)
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants", $"{t.Slug}.db");
            long dbSize = 0;
            if (File.Exists(dbPath))
                dbSize = new FileInfo(dbPath).Length;

            var userCount = await GetTenantUserCountAsync(t.Slug);

            model.Tenants.Add(new TenantHealthItem
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                Status = t.Status,
                PlanName = t.Plan.Name,
                UserCount = userCount,
                DatabaseSizeBytes = dbSize
            });

            model.TotalUsers += userCount;
        }

        return model;
    }

    // ── Extended Tenant Management (Item 15) ─────────────────────────────────

    public async Task<bool> ExtendTrialAsync(Guid tenantId, int days)
    {
        var tenant = await _coreDb.Tenants
            .Include(t => t.ActiveSubscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return false;

        if (tenant.ActiveSubscription is not null)
        {
            tenant.ActiveSubscription.TrialEndsAt = (tenant.ActiveSubscription.TrialEndsAt ?? DateTime.UtcNow).AddDays(days);
        }

        await _coreDb.SaveChangesAsync();
        return true;
    }

    public async Task ScheduleTenantDeletionAsync(Guid tenantId)
    {
        var tenant = await _coreDb.Tenants.FindAsync(tenantId);
        if (tenant is null) return;

        tenant.Status = TenantStatus.Cancelled;
        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.ScheduledDeletionAt = DateTime.UtcNow.AddDays(30); // 30-day grace period
        await _coreDb.SaveChangesAsync();
    }

    // ── Billing Dashboard (Item 17) ──────────────────────────────────────────

    public async Task<BillingDashboardModel> GetBillingDashboardAsync()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var activeSubs = await _coreDb.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync();

        var mrr = activeSubs.Sum(s => s.BillingCycle == BillingCycle.Annual
            ? (s.Plan?.MonthlyPrice ?? 0) * 0.8m // Approximate annual discount
            : s.Plan?.MonthlyPrice ?? 0);

        var trialCount = await _coreDb.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Trialing);
        var pastDueCount = await _coreDb.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.PastDue);
        var cancelledThisMonth = await _coreDb.Subscriptions
            .CountAsync(s => s.Status == SubscriptionStatus.Cancelled && s.CancelledAt >= monthStart);

        var totalRevenue = await _coreDb.Payments
            .Where(p => p.Status == PaymentStatus.Success)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        var planBreakdown = await _coreDb.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            .Include(s => s.Plan)
            .GroupBy(s => s.Plan!.Name)
            .Select(g => new PlanBreakdownItem
            {
                PlanName = g.Key,
                SubscriberCount = g.Count(),
                MonthlyPrice = g.First().Plan!.MonthlyPrice
            })
            .ToListAsync();

        var recentPayments = await _coreDb.Payments
            .Include(p => p.Tenant)
            .OrderByDescending(p => p.TransactionDate)
            .Take(10)
            .Select(p => new RecentPaymentItem
            {
                TenantName = p.Tenant!.Name,
                Amount = p.Amount,
                Currency = p.Currency,
                Status = p.Status.ToString(),
                TransactionDate = p.TransactionDate
            })
            .ToListAsync();

        var recentInvoices = await _coreDb.Invoices
            .Include(i => i.Tenant)
            .OrderByDescending(i => i.IssuedDate)
            .Take(10)
            .Select(i => new RecentInvoiceItem
            {
                TenantName = i.Tenant!.Name,
                InvoiceNumber = i.InvoiceNumber,
                Amount = i.Amount,
                Currency = i.Currency,
                Status = i.Status.ToString(),
                IssuedDate = i.IssuedDate
            })
            .ToListAsync();

        return new BillingDashboardModel
        {
            MonthlyRecurringRevenue = mrr,
            TotalActiveSubscriptions = activeSubs.Count,
            TrialSubscriptions = trialCount,
            PastDueSubscriptions = pastDueCount,
            CancelledThisMonth = cancelledThisMonth,
            TotalRevenueAllTime = totalRevenue,
            PlanBreakdown = planBreakdown,
            RecentPayments = recentPayments,
            RecentInvoices = recentInvoices
        };
    }

    // ── Admin Management (Item 19) ───────────────────────────────────────────

    public async Task<List<SuperAdminListItem>> GetAdminsAsync()
    {
        return await _coreDb.SuperAdmins
            .OrderBy(a => a.Email)
            .Select(a => new SuperAdminListItem
            {
                Id = a.Id,
                Email = a.Email,
                DisplayName = a.DisplayName ?? a.Email,
                IsActive = a.IsActive,
                LastLoginAt = a.LastLoginAt,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    public async Task CreateAdminAsync(string email, string displayName)
    {
        var admin = new Entities.SuperAdmin
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            IsActive = true
        };
        _coreDb.SuperAdmins.Add(admin);
        await _coreDb.SaveChangesAsync();
    }

    public async Task<(bool success, bool isActive)> ToggleAdminStatusAsync(Guid adminId)
    {
        var admin = await _coreDb.SuperAdmins.FindAsync(adminId);
        if (admin is null) return (false, false);

        admin.IsActive = !admin.IsActive;
        await _coreDb.SaveChangesAsync();
        return (true, admin.IsActive);
    }

    // ── Active Sessions (Item 20) ────────────────────────────────────────────

    public async Task<AllSessionsModel> GetAllActiveSessionsAsync()
    {
        var tenants = await _coreDb.Tenants
            .Where(t => t.Status == TenantStatus.Active && !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync();

        var model = new AllSessionsModel();
        var inspectionService = _serviceProvider.GetRequiredService<ITenantInspectionService>();

        foreach (var tenant in tenants)
        {
            try
            {
                var sessions = await inspectionService.GetActiveSessionsAsync(tenant.Slug);
                if (sessions.Count > 0)
                {
                    model.TenantSessions.Add(new TenantSessionSummary
                    {
                        TenantName = tenant.Name,
                        TenantSlug = tenant.Slug,
                        ActiveSessions = sessions.Count,
                        Sessions = sessions
                    });
                    model.TotalSessions += sessions.Count;
                }
            }
            catch
            {
                // Skip tenants with missing/corrupt DBs
            }
        }

        return model;
    }

    // ── Announcements (Item 21) ──────────────────────────────────────────────

    public async Task<Guid> BroadcastAnnouncementAsync(string title, string message, string type, DateTime? expiresAt, string? createdByEmail)
    {
        var announcementType = Enum.TryParse<Entities.AnnouncementType>(type, true, out var parsed)
            ? parsed
            : Entities.AnnouncementType.Info;

        var announcement = new Entities.Announcement
        {
            Id = Guid.NewGuid(),
            Title = title,
            Message = message,
            Type = announcementType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            CreatedByEmail = createdByEmail
        };

        _coreDb.Announcements.Add(announcement);
        await _coreDb.SaveChangesAsync();

        return announcement.Id;
    }

    // ── Export (Item 22) ─────────────────────────────────────────────────────

    public async Task<string> ExportTenantsCsvAsync()
    {
        var tenants = await _coreDb.Tenants
            .Include(t => t.Plan)
            .Include(t => t.ActiveSubscription)
            .OrderBy(t => t.Name)
            .ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Name,Slug,Email,Status,Plan,Subscription,Created");
        foreach (var t in tenants)
        {
            sb.AppendLine($"\"{t.Name}\",\"{t.Slug}\",\"{t.ContactEmail}\",{t.Status},{t.Plan.Name},{t.ActiveSubscription?.Status.ToString() ?? "None"},{t.CreatedAt:yyyy-MM-dd}");
        }

        return sb.ToString();
    }

    public async Task<string> ExportBillingCsvAsync()
    {
        var payments = await _coreDb.Payments
            .Include(p => p.Tenant)
            .OrderByDescending(p => p.TransactionDate)
            .ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Tenant,Amount,Currency,Status,Date,PaystackRef");
        foreach (var p in payments)
        {
            sb.AppendLine($"\"{p.Tenant?.Name}\",{p.Amount},{p.Currency},{p.Status},{p.TransactionDate:yyyy-MM-dd},{p.PaystackReference}");
        }

        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<int> GetTenantUserCountAsync(string slug)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "db", "tenants", $"{slug}.db");
        if (!File.Exists(dbPath)) return 0;

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        await using var tenantDb = new TenantDbContext(options);
        return await tenantDb.Users.CountAsync();
    }
}
