# Tests

Unit and integration tests for the SaaS platform.

## Running

```bash
dotnet test                                                   # All tests (unit + integration)
dotnet test tests/saas.UnitTests/                             # Unit tests only
dotnet test tests/saas.IntegrationTests/                      # Integration tests only
```

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `saas.UnitTests/` | Unit tests | Fast, isolated tests using in-memory SQLite and mocks |
| `saas.IntegrationTests/` | Integration tests | Full app boot via `WebApplicationFactory` + Swap.Testing |
