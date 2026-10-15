# ADR 041 — Migrations are applied by an explicit step, never at startup

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/database.md#migrations`](../database.md#migrations), [`docs/runtime.md#startup`](../runtime.md#startup)

## Context

EF Core makes it trivial to call `context.Database.Migrate()` or `EnsureCreated()` while building the
service graph. Both are wrong here:

- **Two processes.** `Api` and `Worker` are separate processes with separate DI graphs, both of which
  would call it. Two concurrent migrators on one database is a race; EF's migration lock mitigates it but
  does not make a startup migration a schema-management strategy.
- **Least privilege.** The application role would need DDL rights permanently, not just during a deploy.
- **Rollback.** A failed migration at startup means the process does not start, so the failure surfaces as
  an outage rather than as a failed deploy step someone can inspect and revert.
- **`EnsureCreated()`** bypasses migrations entirely: it creates a schema and writes **no**
  `__EFMigrationsHistory` entry, so the database looks permanently "unmigrated" and future migrations
  cannot be applied cleanly.

`docs/database.md#migrations` already states the intent — "Never apply migrations in production
automatically — an explicit deploy step, with a lockfile/`--check` guard".

## Decision

Migrations are applied by an **explicit command**, never by application startup.

- Nothing calls `Migrate()` or `EnsureCreated()`. `AddInfrastructure()` registers the `DbContext` only
  (ADR 004).
- The command is run deliberately, always with an explicit project:

  ```bash
  dotnet ef database update --project src/TourSearch.Infrastructure.Migration
  ```

- The operator supplies the connection through `TOURSEARCH_DB_*` environment variables (ADR 003); the
  design-time factory reads them itself, because `dotnet ef` runs out-of-process.

## Consequences

- Starting the application can never alter the schema, so a schema change is always a separate, visible,
  revertible action.
- The application role can be granted DML rights without DDL rights — the deployment credential and the
  runtime credential can differ.
- **Accepted gap:** the `[PLANNED]` lockfile / `--check` guard from `docs/database.md#migrations` is
  **not implemented**. Nothing today prevents two operators from running `database update` at once. EF's
  own migration lock serialises the application step, so the practical risk is a confused operator rather
  than corruption — but the guard is still owed, and this ADR records the debt rather than implying the
  story is finished.
- The first deployment must apply the migration before the processes start, or they will run against a
  database with no schema. That ordering is a deployment responsibility and is unowned until the
  deployment question (open question 4) is answered.
