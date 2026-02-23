using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using saas.Data.Core;
using saas.Modules.Billing.Entities;
using saas.Modules.Billing.Services;
using Xunit;

namespace saas.Tests.Modules.Billing;

public class DiscountServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();

    public DiscountServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new CoreDbContext(dbOptions);
        _db.Database.EnsureCreated();

        SeedTestData();
    }

    private void SeedTestData()
    {
        var plan = new Plan
        {
            Id = _planId,
            Name = "Pro",
            Slug = "pro",
            MonthlyPrice = 299,
            BillingModel = BillingModel.FlatRate
        };
        _db.Plans.Add(plan);

        _db.Tenants.Add(new saas.Modules.Tenancy.Entities.Tenant
        {
            Id = _tenantId,
            Name = "Test Tenant",
            Slug = "test",
            PlanId = _planId,
            Status = saas.Modules.Tenancy.Entities.TenantStatus.Active,
            ContactEmail = "test@example.com"
        });

        _db.SaveChanges();
    }

    private DiscountService CreateService() =>
        new(_db, NullLogger<DiscountService>.Instance);

    private Discount CreateDiscount(string code = "SAVE10", DiscountType type = DiscountType.Percentage,
        decimal value = 10m, bool isActive = true, int? maxRedemptions = null,
        DateTime? validFrom = null, DateTime? validUntil = null,
        string? applicablePlanSlugs = null, int? durationInCycles = null)
    {
        var discount = new Discount
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Discount {code}",
            Type = type,
            Value = value,
            IsActive = isActive,
            MaxRedemptions = maxRedemptions,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            ApplicablePlanSlugs = applicablePlanSlugs,
            DurationInCycles = durationInCycles
        };
        _db.Discounts.Add(discount);
        _db.SaveChanges();
        return discount;
    }

    // ── ValidateCodeAsync ─────────────────────────────────

    [Fact]
    public async Task Validate_ValidCode_ReturnsIsValid()
    {
        CreateDiscount("VALID10");
        var svc = CreateService();

        var result = await svc.ValidateCodeAsync("VALID10", _tenantId);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Discount);
    }

    [Fact]
    public async Task Validate_NonExistentCode_ReturnsInvalid()
    {
        var svc = CreateService();
        var result = await svc.ValidateCodeAsync("NOPE", _tenantId);

        Assert.False(result.IsValid);
        Assert.Contains("not found", result.Error!);
    }

    [Fact]
    public async Task Validate_InactiveCode_ReturnsInvalid()
    {
        CreateDiscount("INACTIVE", isActive: false);
        var svc = CreateService();

        var result = await svc.ValidateCodeAsync("INACTIVE", _tenantId);
        Assert.False(result.IsValid);
        Assert.Contains("no longer active", result.Error!);
    }

    [Fact]
    public async Task Validate_ExpiredCode_ReturnsInvalid()
    {
        CreateDiscount("EXPIRED", validUntil: DateTime.UtcNow.AddDays(-1));
        var svc = CreateService();

        var result = await svc.ValidateCodeAsync("EXPIRED", _tenantId);
        Assert.False(result.IsValid);
        Assert.Contains("expired", result.Error!);
    }

    [Fact]
    public async Task Validate_FutureCode_ReturnsInvalid()
    {
        CreateDiscount("FUTURE", validFrom: DateTime.UtcNow.AddDays(5));
        var svc = CreateService();

        var result = await svc.ValidateCodeAsync("FUTURE", _tenantId);
        Assert.False(result.IsValid);
        Assert.Contains("not yet valid", result.Error!);
    }

    [Fact]
    public async Task Validate_MaxRedemptionsReached_ReturnsInvalid()
    {
        var discount = CreateDiscount("MAXED", maxRedemptions: 2);
        discount.CurrentRedemptions = 2;
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.ValidateCodeAsync("MAXED", _tenantId);
        Assert.False(result.IsValid);
        Assert.Contains("maximum redemptions", result.Error!);
    }

    [Fact]
    public async Task Validate_WrongPlan_ReturnsInvalid()
    {
        CreateDiscount("PLANONLY", applicablePlanSlugs: "enterprise");
        var svc = CreateService();

        var result = await svc.ValidateCodeAsync("PLANONLY", _tenantId, _planId);
        Assert.False(result.IsValid);
        Assert.Contains("not applicable", result.Error!);
    }

    [Fact]
    public async Task Validate_AlreadyApplied_ReturnsInvalid()
    {
        var discount = CreateDiscount("DUPE");
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.ValidateCodeAsync("DUPE", _tenantId);
        Assert.False(result.IsValid);
        Assert.Contains("already applied", result.Error!);
    }

    // ── ApplyAsync ────────────────────────────────────────

    [Fact]
    public async Task Apply_ValidCode_CreatesTenantDiscount()
    {
        var discount = CreateDiscount("APPLY10", durationInCycles: 3);
        var svc = CreateService();

        var td = await svc.ApplyAsync(_tenantId, "APPLY10");

        Assert.Equal(_tenantId, td.TenantId);
        Assert.Equal(discount.Id, td.DiscountId);
        Assert.True(td.IsActive);
        Assert.Equal(3, td.RemainingCycles);

        // Check redemption count incremented
        var fresh = await _db.Discounts.FindAsync(discount.Id);
        Assert.Equal(1, fresh!.CurrentRedemptions);
    }

    [Fact]
    public async Task Apply_InvalidCode_ThrowsInvalidOperation()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApplyAsync(_tenantId, "DOESNOTEXIST"));
    }

    // ── CalculateDiscountAsync ────────────────────────────

    [Fact]
    public async Task Calculate_PercentageDiscount_CalculatesCorrectly()
    {
        var discount = CreateDiscount("PCT20", DiscountType.Percentage, value: 20);
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.CalculateDiscountAsync(_tenantId, 200m);
        Assert.Equal(40m, result); // 20% of 200
    }

    [Fact]
    public async Task Calculate_FixedDiscount_CappedAtSubtotal()
    {
        var discount = CreateDiscount("FIXED500", DiscountType.FixedAmount, value: 500);
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.CalculateDiscountAsync(_tenantId, 100m);
        Assert.Equal(100m, result); // Fixed 500 capped at subtotal 100
    }

    [Fact]
    public async Task Calculate_NoActiveDiscounts_ReturnsZero()
    {
        var svc = CreateService();
        var result = await svc.CalculateDiscountAsync(_tenantId, 200m);
        Assert.Equal(0m, result);
    }

    [Fact]
    public async Task Calculate_ZeroSubtotal_ReturnsZero()
    {
        var discount = CreateDiscount("ANY");
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.CalculateDiscountAsync(_tenantId, 0m);
        Assert.Equal(0m, result);
    }

    // ── DecrementCyclesAsync ──────────────────────────────

    [Fact]
    public async Task DecrementCycles_ReducesCount()
    {
        var discount = CreateDiscount("CYCLE3");
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            RemainingCycles = 3,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.DecrementCyclesAsync(_tenantId);

        var td = await _db.TenantDiscounts.FirstAsync(d => d.TenantId == _tenantId);
        Assert.Equal(2, td.RemainingCycles);
        Assert.True(td.IsActive);
    }

    [Fact]
    public async Task DecrementCycles_LastCycle_DeactivatesDiscount()
    {
        var discount = CreateDiscount("CYCLE1");
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            RemainingCycles = 1,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.DecrementCyclesAsync(_tenantId);

        var td = await _db.TenantDiscounts.FirstAsync(d => d.TenantId == _tenantId);
        Assert.Equal(0, td.RemainingCycles);
        Assert.False(td.IsActive);
    }

    // ── RemoveAsync ───────────────────────────────────────

    [Fact]
    public async Task Remove_ActiveDiscount_DeactivatesIt()
    {
        var discount = CreateDiscount("REMOVE");
        _db.TenantDiscounts.Add(new TenantDiscount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DiscountId = discount.Id,
            AppliedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        await svc.RemoveAsync(_tenantId, discount.Id);

        var td = await _db.TenantDiscounts.FirstAsync(d => d.DiscountId == discount.Id);
        Assert.False(td.IsActive);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
