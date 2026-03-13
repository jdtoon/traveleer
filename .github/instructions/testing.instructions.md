---
applyTo: "**"
---

# Testing Requirements - HTMX + Razor Partials

Stack: ASP.NET Core .NET 10 · Swap.Htmx · Swap.Testing · DaisyUI 5 · Tailwind CSS v4 · SQLite core/audit plus per-tenant SQLite.

Tests must simulate how a real user experiences Traveleer: full page shell, HTMX partial loading, modal interactions, form submissions, tenant boundaries, and success or failure feedback.

---

## Hard Rules

- Build must pass before ending a feature turn: `dotnet build src/saas.csproj`
- Unit tests must pass: `dotnet test tests/saas.UnitTests`
- Integration tests must pass: `dotnet test tests/saas.IntegrationTests`
- Any UI, HTMX, or user-facing workflow is incomplete without manual browser QA.
- Integration tests are the ground truth for pages, partials, modals, and HTMX refresh behavior.
- After the entry route, navigate through the app like a user: links, buttons, menus, tabs, modals, and in-app actions.
- For migrated AgencyCardPro features, test the redesigned Traveleer behavior, not just a server-side happy path.

---

## Test File Location

Mirror the source module structure as closely as practical:

```text
tests/saas.IntegrationTests/Modules/{Module}/{Feature}IntegrationTests.cs
```

Examples:

- `src/Modules/Bookings/Controllers/BookingController.cs`
  -> `tests/saas.IntegrationTests/Modules/Bookings/BookingIntegrationTests.cs`
- `src/Modules/Branding/Controllers/BrandingController.cs`
  -> `tests/saas.IntegrationTests/Modules/Branding/BrandingIntegrationTests.cs`

---

## Fixture Setup

Always use `AppFixture` via `IClassFixture<AppFixture>`.

```csharp
public class BookingIntegrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;
    private readonly HtmxTestClient<Program> _client;

    public BookingIntegrationTests(AppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateTenantClient("demo");
    }
}
```

Use:

- `CreateClient()` for public routes such as marketing, login, registration, or public callbacks.
- `CreateTenantClient("demo")` for tenant routes under `/{slug}/...`.

When role-specific behavior matters, add fixture helpers or explicit authenticated clients rather than skipping the scenario.

---

## Four Mandatory Integration Test Layers

Every new page + partial combination requires all four layers.

### Layer 1 - Full Page

A browser navigation request must return the full layout and target containers.

```csharp
[Fact]
public async Task BookingsPage_RendersFullLayout()
{
    var response = await _client.GetAsync("/demo/bookings");

    await response
        .AssertSuccess()
        .AssertContainsAsync("<html")
        .AssertElementExistsAsync("#main-content")
        .AssertElementExistsAsync("#modal-container");
}
```

### Layer 2 - Partial Isolation

An HTMX request must return a partial without the site layout.

```csharp
[Fact]
public async Task BookingListPartial_RendersWithoutLayout()
{
    var response = await _client.HtmxGetAsync("/demo/bookings/list");

    await response
        .AssertSuccess()
        .AssertDoesNotContainAsync("<html")
        .AssertElementExistsAsync("table")
        .AssertContainsAsync("Bookings");
}
```

### Layer 3 - User Flow

Test the journey: load page, trigger partials, open modal, submit, and verify the response behavior.

```csharp
[Fact]
public async Task BookingsPage_UserCanOpenCreateForm()
{
    var page = await _client.GetAsync("/demo/bookings");
    await page.AssertSuccess().AssertElementExistsAsync("#modal-container");

    var form = await _client.HtmxGetAsync("/demo/bookings/new");
    await form
        .AssertSuccess()
        .AssertDoesNotContainAsync("<html")
        .AssertElementExistsAsync("dialog.modal");
}
```

### Layer 4 - Database Verification

Every operation that writes to the database must verify the persisted state. HTTP response assertions alone are not sufficient — the test must open the tenant database and confirm that entities were created, updated, or deleted with the correct field values.

Add a private `OpenTenantDb()` helper to every integration test class that performs write operations:

```csharp
private TenantDbContext OpenTenantDb()
{
    var options = new DbContextOptionsBuilder<TenantDbContext>()
        .UseSqlite($"Data Source={_fixture.GetTenantDbPath(TenantSlug)}")
        .Options;
    return new TenantDbContext(options);
}
```

#### Create verification

```csharp
[Fact]
public async Task CreateClient_OnValidSubmit_PersistsToDatabase()
{
    var uniqueName = $"DB-Test-{Guid.NewGuid():N}"[..20];
    var formResponse = await _client.HtmxGetAsync($"/{TenantSlug}/clients/new");
    var response = await _client.SubmitFormAsync(formResponse, "form", new Dictionary<string, string>
    {
        ["Name"] = uniqueName,
        ["Email"] = $"{uniqueName}@test.local"
    });

    response.AssertSuccess();

    await using var db = OpenTenantDb();
    var client = await db.Clients.SingleAsync(c => c.Name == uniqueName);
    Assert.NotEqual(Guid.Empty, client.Id);
    Assert.Equal($"{uniqueName}@test.local", client.Email);
    Assert.NotNull(client.CreatedAt);
}
```

#### Update verification

```csharp
await using var db = OpenTenantDb();
var entity = await db.Entities.SingleAsync(e => e.Id == id);
Assert.Equal("updated value", entity.Name);
Assert.NotNull(entity.UpdatedAt);
```

#### Delete verification

```csharp
await using var db = OpenTenantDb();
Assert.False(await db.Entities.AnyAsync(e => e.Id == id));
```

#### Status transition verification

```csharp
await using var db = OpenTenantDb();
var entity = await db.Entities.SingleAsync(e => e.Id == id);
Assert.Equal(ExpectedStatus.Active, entity.Status);
Assert.NotNull(entity.ActivatedAt);
```

This layer closes the gap between "the HTTP response looked right" and "the data was actually saved correctly." It replaces the need for manual database inspection during QA.

---

## Form Submission Testing

Every form must have valid and invalid submission coverage.

### Valid submission

```csharp
[Fact]
public async Task CreateBookingForm_OnValidSubmit_ClosesModalAndRefreshesList()
{
    var form = new Dictionary<string, string>
    {
        ["ClientId"] = Guid.NewGuid().ToString(),
        ["Reference"] = "TRV-001"
    };

    var response = await _client.HtmxPostAsync("/demo/bookings", form);

    response.AssertSuccess();
    await response.AssertHasTriggerAsync("bookings.refresh");
}
```

### Invalid submission

```csharp
[Fact]
public async Task CreateBookingForm_OnInvalidSubmit_ReturnsFormWithErrors()
{
    var response = await _client.HtmxPostAsync("/demo/bookings", new Dictionary<string, string>());

    await response
        .AssertSuccess()
        .AssertDoesNotContainAsync("<html")
        .AssertElementExistsAsync("dialog.modal");
}
```

For important fields, assert specific inline validation feedback rather than only checking that the request stayed 200.

---

## Access Control Testing

Every protected page needs at least one unauthenticated or unauthorized access test.

```csharp
[Fact]
public async Task BookingsPage_WhenUnauthenticated_Redirects()
{
    var publicClient = _fixture.CreateClient();
    var response = await publicClient.GetAsync("/demo/bookings");

    response.AssertStatus(System.Net.HttpStatusCode.Redirect);
}
```

---

## Migration-Specific Coverage

For AgencyCardPro migrations, tests must also verify the redesign contract:

- tenant scoping is preserved
- Traveleer layouts and HTMX targets are present
- branding-aware pages still render correctly with default tenant settings
- workflow state changes survive the move from single-tenant assumptions to multi-tenant storage
- imports, numbering, and document generation are validated through service tests and targeted integration coverage

Do not treat a direct logic port as sufficient proof of correctness.

---

## Manual Browser QA

After automated tests pass, manually verify changed UX in the browser with Playwright.

Required checks:

1. Layout quality on desktop and mobile.
2. HTMX transitions feel smooth and keep the page coherent.
3. Forms show inline field errors, not generic crashes.
4. Success actions emit visible feedback such as toasts, refreshed lists, or modal close.
5. Empty states explain the next action.
6. No stale content remains in `#modal-container`.
7. Tenant navigation works through in-app links and buttons.
8. No raw exceptions, stack traces, or broken layouts appear.
9. Sensitive data is not exposed in URLs, hidden fields, or HTML comments.
10. Regress adjacent flows after the changed feature works.

---

## Definition Of Done

A migrated or new feature is not done until all of the following are true:

- `dotnet build src/saas.csproj` exits successfully
- `dotnet test tests/saas.UnitTests` exits successfully
- `dotnet test tests/saas.IntegrationTests` exits successfully
- full-page integration coverage exists
- partial isolation coverage exists
- user-flow coverage exists
- database verification exists for every write operation (create, update, delete, status change)
- valid and invalid form submissions are tested where forms exist
- access control is tested for protected routes
- manual Playwright QA has been completed
- adjacent flows have been regression-checked

---

## Anti-Patterns

- Do not test only the happy path.
- Do not skip full-page tests because the partial works.
- Do not assert only status codes when the DOM contract matters.
- Do not manually type internal tenant URLs during browser QA after entry unless the scenario specifically starts there.
- Do not copy AgencyCardPro tests blindly if the feature has been redesigned in Traveleer.
- Do not end a feature turn with failing tests.
- Do not assert only HTTP responses when the operation writes to the database. Open `TenantDbContext` and verify persisted state.
