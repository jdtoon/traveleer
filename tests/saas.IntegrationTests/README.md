# Integration Tests

Full-stack integration tests using `Swap.Testing` and `WebApplicationFactory<Program>`.

## Setup

The `AppFixture` boots the entire application in Development mode with:
- All providers mocked (Email, Billing, Turnstile)
- Local filesystem storage
- DevSeed enabled (provisions a `demo` tenant)

## Writing Tests

```csharp
public class MyTests : IClassFixture<AppFixture>
{
    private readonly HtmxTestClient<Program> _client;

    public MyTests(AppFixture fixture)
    {
        _client = fixture.CreateClient();  // Public routes
        // or: fixture.CreateTenantClient("demo")  // Tenant routes
    }

    [Fact]
    public async Task MyPage_Works()
    {
        // Browser request (full page)
        var response = await _client.GetAsync("/my-page");
        await response.AssertSuccess().AssertContainsAsync("expected text");

        // HTMX request (partial)
        var htmxResponse = await _client.HtmxGetAsync("/my-page");
        await htmxResponse.AssertSuccess().AssertDoesNotContainAsync("<html");
    }
}
```

## Assertion Methods

| Method | Purpose |
|--------|---------|
| `.AssertSuccess()` | 2xx status |
| `.AssertStatus(code)` | Specific status |
| `.AssertContainsAsync(text)` | Body contains text |
| `.AssertElementExistsAsync(selector)` | CSS selector match |
| `.AssertHasOobSwapAsync(targetId)` | OOB swap present |
| `.AssertHasTriggerAsync(event)` | HX-Trigger header |
