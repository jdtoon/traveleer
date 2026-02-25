# Tests

Unit and integration tests for the SaaS platform.

## Running

```bash
dotnet test                                                   # All tests (unit + integration)
dotnet test tests/saas.UnitTests/                             # Unit tests only
dotnet test tests/saas.IntegrationTests/                      # Integration tests only
dotnet test --filter "FullyQualifiedName~Billing"             # Run tests matching a pattern
dotnet test --filter "FullyQualifiedName!~Integration"        # Exclude integration tests
```

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `saas.UnitTests/` | Unit tests | Fast, isolated tests using in-memory SQLite and stubs |
| `saas.IntegrationTests/` | Integration tests | Full app boot via `WebApplicationFactory` + Swap.Testing |

## Unit Test Conventions

### Class Lifecycle

Tests use xUnit with one of two async lifecycle patterns:

**Pattern A — `IAsyncDisposable`** (most common):

```csharp
public class MyServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CoreDbContext _db;

    public MyServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection).Options;
        _db = new CoreDbContext(opts);
        _db.Database.EnsureCreated();
        // Seed test data
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
```

**Pattern B — `IAsyncLifetime`** (when async init is needed):

```csharp
public class MyServiceTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Async setup (ServiceCollection, DI, etc.)
    }
    public async Task DisposeAsync()
    {
        // Cleanup
    }
}
```

### Database Setup

- **CoreDbContext / TenantDbContext:** SQLite in-memory (`DataSource=:memory:`) with an open connection held for the test class lifetime
- **AuditDbContext:** Temporary file-based SQLite (avoids concurrency issues with the background audit writer channel)
- Each test class creates its own isolated database — no shared state between test classes
- Call `_db.Database.EnsureCreated()` to apply the schema without migrations

### Stub/Fake Patterns

Services use **private nested stub classes** implementing the required interface:

```csharp
private class StubEmailService : IEmailService
{
    public bool SendCalled { get; private set; }
    public Task SendAsync(EmailMessage message) { SendCalled = true; return Task.CompletedTask; }
    // ... other methods
}
```

Common patterns:
- **Behavioral flags** (`bool XxxCalled`) to verify interactions without asserting on exact calls
- **Configurable returns** (`public SomeResponse? NextResult { get; set; }`) to control stub behavior per test
- **DB-aware stubs** that accept `CoreDbContext` when they need to persist entities (e.g. `StubInvoiceEngine`)
- **Overriding virtual methods** (`override` instead of `new`) when stubbing concrete classes like `PaystackClient`

### Test Data Seeding

Seed test data in the constructor using pre-declared `Guid` fields:

```csharp
private readonly Guid _tenantId = Guid.NewGuid();
private readonly Guid _planId = Guid.NewGuid();

public MyServiceTests()
{
    // ... DB setup ...
    _db.Plans.Add(new Plan { Id = _planId, Name = "Test", Slug = "test", ... });
    _db.Tenants.Add(new Tenant { Id = _tenantId, PlanId = _planId, ... });
    _db.SaveChanges();
}
```

### File Naming

Test files mirror the source structure:

```
src/Modules/Billing/Services/DunningService.cs
    → tests/saas.UnitTests/Modules/Billing/DunningServiceTests.cs

src/Infrastructure/Services/MockBillingService.cs
    → tests/saas.UnitTests/Infrastructure/MockBillingServiceTests.cs
```

### Assertions

Standard xUnit `Assert.*` methods plus EF queries for database state verification:

```csharp
Assert.True(result.Success);
Assert.Equal(500m, breakdown.Total);
Assert.Single(items, i => i.Type == LineItemType.Seat);

// DB state verification
var invoice = await _db.Invoices.FindAsync(result.InvoiceId);
Assert.Equal(InvoiceStatus.Paid, invoice!.Status);
```

## Integration Test Conventions

See [saas.IntegrationTests/README.md](saas.IntegrationTests/README.md) for details.

Key points:
- Uses `IClassFixture<AppFixture>` for shared app bootstrap
- `AppFixture` boots the full app with mock providers (`Billing:Mock`, `Email:Console`, `Turnstile:Mock`)
- DevSeed creates a `demo` tenant with `admin@demo.local` on the `starter` plan
- Two client types: `CreateClient()` (public routes) and `CreateTenantClient("demo")` (tenant routes)
- Uses `Swap.Testing` assertion methods: `.AssertSuccess()`, `.AssertContainsAsync()`, `.AssertElementExistsAsync()`, `.AssertHasOobSwapAsync()`, `.AssertHasTriggerAsync()`

## Adding Tests for a New Module

1. Create `tests/saas.UnitTests/Modules/YourModule/YourServiceTests.cs`
2. Follow Pattern A (IAsyncDisposable) with in-memory SQLite
3. Use `TenantDbContext` for tenant-scoped entities, `CoreDbContext` for platform entities
4. Create stubs for any injected interfaces
5. Seed minimal test data in the constructor
6. One `[Fact]` per behavior — name as `MethodName_Scenario_ExpectedResult`
