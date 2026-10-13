# ADR 036 — Test parallelization policy

- **Status:** **Accepted** — decided 2026-10-13
- **Date:** 2026-10-13
- **Stated in:** [`docs/conventions.md#testing`](../conventions.md#testing)
- **Supersedes:** the `Proposed` version of this same file

> This file originally reserved the id and decided nothing, because the policy is not stated anywhere
> in the specification and choosing it inside a documentation-backfill task would have been inventing a
> decision. The first real test file (task `002`) forced the choice, so it is made here — for the unit
> project only. The integration project is still undecided, and its `INTERIM` `NoWarn` stays.

## Context

`MSTest` 4.0.2 ships an analyzer that requires every test assembly to state its parallelization policy
explicitly. `[FACT]` verified by reproduction: with `TreatWarningsAsErrors` on and no policy declared,
the build fails with

```
CSC : error MSTEST0001: Явное включение или отключение параллелизации тестов
```

Because `Directory.Build.props` sets `TreatWarningsAsErrors`, the diagnostic becomes a build error for
every test project.

Today neither test project contains a single `.cs` file, so there is nothing to parallelize and the
diagnostic is a false positive. Both projects therefore carry an **interim** suppression:

```xml
<NoWarn>$(NoWarn);MSTEST0001</NoWarn>
```

Both `.csproj` comments state this line must **not** remain once real tests exist, and that the durable
fix is an assembly-level attribute in a real test file.

`docs/conventions.md#testing` does not mention parallelization at all, so the specification gives no
answer. The two projects are also **not symmetric**:

| Project | Constraint |
|---|---|
| `TourSearch.UnitTests` | Pure domain logic + parser against committed fixtures. No shared resource across tests, so parallelization is plausible. |
| `TourSearch.IntegrationTests` | Shares **one** Testcontainers PostgreSQL instance across all tests. `[assembly: Parallelize]` over a shared database is a flakiness hazard — tests would race on the same schema and rows. |

So one policy for both projects may be wrong: the answer for the unit project need not be the answer for
the integration project.

## Decision

**Decision — the unit test project parallelizes at method level.**

`tests/TourSearch.UnitTests/AssemblyInfo.cs` declares:

```csharp
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

and the interim `<NoWarn>$(NoWarn);MSTEST0001</NoWarn>` has been **removed** from
`tests/TourSearch.UnitTests/TourSearch.UnitTests.csproj`, so the analyzer now enforces the policy rather
than being silenced.

Reasoning: the unit tests added in task `002` are in-memory assertions over the CLR model and the
generated DDL script (`GenerateCreateScript()`, `context.Model`). They share no state — no database, no
file, no server, no static mutable field — so method-level parallelization is safe and shortens the run.

**Still undecided — the integration test project.** Its `<NoWarn>$(NoWarn);MSTEST0001</NoWarn>` remains
and must **not** be removed until that project has a test file, because it shares **one** Testcontainers
PostgreSQL instance across all tests. `[assembly: Parallelize]` over a shared database is a flakiness
hazard (tests racing on the same schema and rows), so its answer is a separate decision, not a copy of
this one. Options when it is taken:

- `[assembly: DoNotParallelize]` — simplest, slowest, correct by construction.
- `[assembly: Parallelize]` with per-class isolation, if the Testcontainers fixture is ever reworked to
give each parallel unit its own database.

`[FACT]` both options silence `MSTEST0001` — verified by reproduction against MSTest 4.0.2 (see the
previous version of this file's findings, retained below).

## Consequences

- `MSTEST0001` is resolved in the unit project by a **real declaration**, not by suppression. A future
  contributor who deletes the attribute gets a build error, which is the intended behaviour.
- The unit suite runs faster and stays deterministic, because it shares no state.
- The integration project keeps its interim suppression, and its comment says why. That suppression is a
  known, tracked debt tied to open question 12, which remains partly open.
- **Open question 12 stays open for the integration project** and must be resolved when its first test
  file is added; the answer here is a precedent, not a template.
- The earlier references in both `.csproj` files to "open question 12" in `docs/open-questions.md` are
  now correct: question 12 exists and points at this ADR.
