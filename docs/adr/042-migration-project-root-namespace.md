# ADR 042 — The migration project's root namespace is `TourSearch`, not `TourSearch.Infrastructure.Migration`

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/architecture.md#why-migration-must-not-redeclare-dbcontext-decided`](../architecture.md#why-migration-must-not-redeclare-dbcontext-decided)
- **Related:** [ADR 002](./002-migration-references-runtime-dbcontext.md)

## Context

`dotnet ef migrations add` places generated migrations in a `Migrations` namespace **under the project's
root namespace**. With the natural root namespace of the project
(`TourSearch.Infrastructure.Migration`), the generated files land in
`TourSearch.Infrastructure.Migration.Migrations`.

That namespace makes the generated code **uncompilable**. `TourSearch.Infrastructure.Migration` resolves
`Migration` to itself — the namespace segment `Migration` — instead of
`Microsoft.EntityFrameworkCore.Migrations.Migration`, so the generated
`public partial class InitialSchema : Migration` fails. `[FACT]` verified by reproduction with
`Microsoft.EntityFrameworkCore.Relational` 10.0.12:

```
error CS0118: "Migration" является пространством имен, но используется как тип
```

Nested namespaces win over file-level `using` directives, so **no** `using` alias in the generated file
can fix it, and the generated file is not ours to edit (it is regenerated on every `migrations add`).

## Decision

`TourSearch.Infrastructure.Migration.csproj` sets:

```xml
<RootNamespace>TourSearch</RootNamespace>
```

so EF's default output namespace is `TourSearch.Migrations` and the documented command

```bash
dotnet ef migrations add <Name> --project src/TourSearch.Infrastructure.Migration
```

works with **no** extra flag and no hand-editing.

The project **name**, directory and dependency direction are unchanged; only the namespace changes.

## Consequences

- The standard command in `docs/architecture.md#migration-commands` works as written, which matters more
  than namespace tidiness: an operator following the docs must not hit `CS0118`.
- Migrations live in `TourSearch.Migrations`, which is a normal, readable location and matches the
  convention most EF projects use.
- The design-time factory remains in `TourSearch.Infrastructure.Migration` (its own explicit namespace,
  unaffected), so the factory and the migrations are distinguishable.
- **This is a deliberate mismatch between the assembly name/path and the root namespace.** A reader may
  expect `TourSearch.Infrastructure.Migration.*` and find `TourSearch.Migrations` instead. The `.csproj`
  carries a comment pointing here so the reason is discoverable at the point of confusion.
- Renaming the project or directory to match (`TourSearch.Migrations`) is a possible simplification, but it
  would churn paths referenced by `AGENTS.md` §4, `docs/architecture.md`, `docs/database.md` and every
  migration command in the docs. Not worth it for a namespace; revisit only if the project is moved
  anyway.
