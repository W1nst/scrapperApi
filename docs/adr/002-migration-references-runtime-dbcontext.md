# ADR 002 — Migration references the runtime `DbContext`; no second model

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/architecture.md#why-migration-must-not-redeclare-dbcontext-decided`](../architecture.md#why-migration-must-not-redeclare-dbcontext-decided), [`docs/database.md#where-this-dbcontext-lives`](../database.md#where-this-dbcontext-lives)

## Context

An earlier version declared `TourSearchDbContext` **inside** the Migration project. That creates two
`OnModelCreating()` definitions — the migration model and the runtime model. They drift apart silently,
and migrations then generate tables the runtime does not expect.

`docs/database.md#where-this-dbcontext-lives` states the rule directly: exactly one `DbContext` in the
system, and it lives in `TourSearch.Infrastructure`.

## Decision

There is exactly **one** `TourSearchDbContext`, and it lives in `TourSearch.Infrastructure`.

`TourSearch.Infrastructure.Migration` references it through an `IDesignTimeDbContextFactory<TourSearchDbContext>`.
It declares **only** the design-time factory and the migrations — it does not redeclare the model.

```
TourSearch.Infrastructure.Migration/
├── DesignTimeDbContext.cs                        # IDesignTimeDbContextFactory<TourSearchDbContext> only
├── Migrations/
└── TourSearch.Infrastructure.Migration.csproj    # depends on TourSearch.Infrastructure
```

Migrations are always run with an explicit project flag:

```bash
dotnet ef migrations add <Name> --project src/TourSearch.Infrastructure.Migration
```

## Consequences

- The migration model and the runtime model cannot drift — there is only one model.
- The design-time factory has to build its own context, which is why the provider packages are visible
  in the Migration project even though `Infrastructure` also declares them.
- A migration that does not match the runtime model is impossible to produce accidentally; if it
  happens, the cause is elsewhere (a shadow property, a convention).
- Two `OnModelCreating()` definitions in the tree are a defect, not a style issue. Add a test that
  asserts a single `DbContext` type if this ever regresses.
- Enforced by the build graph: `TourSearch.Infrastructure.Migration.csproj` has no
  `Microsoft.NET.Sdk.Web`/`Api` reference and carries a comment recording the rule.
