# Tests

Unit and integration tests for the SaaS platform.

## Running

```bash
dotnet test                                           # All tests (unit + integration)
dotnet test tests/saas.Tests.csproj                   # Unit tests only (151 tests)
dotnet test tests/saas.IntegrationTests/              # Integration tests only (10 tests)
dotnet test --filter "Category=Integration"           # Filter by trait
```

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `saas.Tests/` | Unit tests | Fast, isolated tests using in-memory SQLite and mocks |
| `saas.IntegrationTests/` | Integration tests | Full app boot via `WebApplicationFactory` + Swap.Testing |

## Unit Test Structure (saas.Tests/)

```
Data/           — DbContext and seeder tests
Infrastructure/ — Middleware, provisioner, service tests
Modules/        — Module-specific service tests
Shared/         — Contract and definition tests
Integration/    — Legacy integration tests (being migrated)
```

## Integration Test Structure (saas.IntegrationTests/)

```
Fixtures/    — AppFixture (shared WebApplicationFactory setup)
Marketing/   — Public page smoke tests
Notes/       — Tenant module CRUD tests
```

Uses `Swap.Testing` for `HtmxTestClient<Program>` and `HtmxTestResponse` assertions.
