# ADR 040 — One shared DbContext options path for the design-time and runtime graphs

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/database.md#conventions`](../database.md#conventions), [`docs/architecture.md#migration-commands`](../architecture.md#migration-commands)

## Context

`TourSearchDbContext` is built on two independent paths:

- the **runtime** graph, through `AddInfrastructure()`, used by `Api` and `Worker`;
- the **design-time** factory, which `dotnet ef` runs in a **separate process** that sees neither the
  application's DI graph nor its secret provider.

Both must produce an **identical** model, or migrations stop matching what the runtime expects.

The specific hazard is `snake_case`. `[FACT]` verified by reproduction on
`EFCore.NamingConventions` 10.0.1: `UseSnakeCaseNamingConvention()` is an extension on
`DbContextOptionsBuilder`, **not** on `ModelBuilder` — `modelBuilder.UseSnakeCaseNamingConvention()` does
not compile (`CS1929`). Because it is an **option**, it only applies if the caller chains it. A path that
forgets it is not a compile error and not a runtime error: it silently produces `PascalCase` column and
index names, so the migration creates `ParametersHash` while the runtime model says
`parameters_hash` — or the reverse. Table names are unaffected, because `ToTable("snake_case")` is
explicit.

This risk was identified in task `002` and recorded in its `## Outcome` as residual, because
`AddInfrastructure()` did not exist yet.

## Decision

The provider and the naming convention are configured in **exactly one place** —
`TourSearchDbContextOptionsFactory` in `TourSearch.Infrastructure`:

```csharp
builder.UseNpgsql(dataSource, configure);
builder.UseSnakeCaseNamingConvention();
```

- `AddInfrastructure()` builds the runtime `DbContext` through it.
- `DesignTimeDbContextFactory` builds the design-time `DbContext` through it.
- **A second copy of `UseNpgsql(...).UseSnakeCaseNamingConvention()` anywhere in the code base is a
  defect**, not a style issue.

The factory takes an `NpgsqlDataSource` and an optional `Action<NpgsqlDbContextOptionsBuilder>`, which the
design-time path uses to set `MigrationsAssembly` — the one thing the two paths legitimately differ on.

## Consequences

- The two paths cannot drift by omission. A missing `UseSnakeCaseNamingConvention()` is now impossible on
  one path only, because there is only one path.
- The design-time factory stays three lines: read the environment, build the options, construct the
  context. It contains **no** model and **no** `OnModelCreating` (ADR 002).
- A test asserts the runtime path produces snake_case names, so a regression fails in CI rather than in a
  migration.
- Adding a future provider option (retry strategy, logging, `EnableSensitiveDataLogging` in tests) has one
  obvious home instead of two that must be kept in sync.
- **Known deviation:** `tests/TourSearch.UnitTests/Infrastructure/ModelShapeTests.cs` still builds its own
  chain. It is a test that deliberately inspects the model it constructs, and it predates this ADR. It is a
  third occurrence and should be switched to the shared factory when that test is next touched — recorded
  rather than silently fixed here, because changing a passing test's construction path mid-task would
  conflate two changes.
