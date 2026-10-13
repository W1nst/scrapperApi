# ADR 037 — `SearchParameters` is an EF Core 10 complex type mapped to JSONB

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/domain.md#searchparameters`](../domain.md), [`docs/database.md`](../database.md)

## Context

`SearchParameters` is a value object owned by `SearchJob`: eight fields, always read and written together
with its job, never queried on its own. It must persist as a single `jsonb` column rather than a table.

Two EF APIs produce that column, and the specification disagreed with itself about which:

- `docs/database.md` used `b.ComplexProperty(x => x.Parameters, p => p.ToJson())` and stated that
  `OwnsOne(...).ToJson()` is "the pre-EF-10 owned-entity form — deprecated".
- `docs/domain.md` still described `Parameters` as an "owned entity → JSONB".

`OwnsOne` and `ComplexProperty` are not interchangeable: `OwnsOne` creates a tracked entity with a
shadow key and its own change-tracking identity, while a complex type is a structural value with no
identity. Choosing one silently would leave a lasting difference in behaviour that nothing in the docs
explains.

## Decision

`SearchParameters` is mapped with **`ComplexProperty(...).ToJson()`** (EF Core 10), producing a single
`jsonb` column named `parameters` on `search_jobs`.

`docs/domain.md` is corrected to match, and `docs/database.md` wins the contradiction.

`[FACT]` verified by reproduction on `Microsoft.EntityFrameworkCore` 10.0.12 +
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3: the generated DDL contains `parameters jsonb NOT NULL`,
and no `parameters` table is generated.

## Consequences

- One column, no join, no shadow key, no separate change-tracking identity — matching how the value is
  actually used.
- **Accepted limitation: a complex type has no identity and its members cannot be columns.** A query
  such as "find all jobs for region X" cannot be served from inside the JSON. Lookups go through
  `ParametersHash` (exact-match dedupe, indexed) instead. If a future feature needs to filter or sort by
  an individual parameter, that needs an explicit column — and its own ADR, because it changes the shape
  of the write path.
- The eight members are **not** independently updatable by a partial `ExecuteUpdate`; the whole JSON is
  rewritten. Since parameters are immutable after job creation, this costs nothing today.
- Because the value cannot be queried, parameter ranges are not enforced anywhere (see
  [ADR 038](./038-parameter-validation-at-tool-layer.md)); the two decisions reinforce each other.
- EF Core 10 is required. On EF 9 or earlier the equivalent mapping is `OwnsOne(...).ToJson()`, so a
  downgrade would silently change the model and needs its own ADR.
