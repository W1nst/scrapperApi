# ADR 036 — Test parallelization policy (undecided)

- **Status:** **Proposed** — the policy is **not decided**
- **Date:** 2026-10-13
- **Stated in:** [`docs/conventions.md#testing`](../conventions.md#testing) (silent on this) and the `INTERIM` comments in both test `.csproj` files

> This file exists to **reserve the id** and record the question. It deliberately decides nothing.
> Both `tests/TourSearch.UnitTests/TourSearch.UnitTests.csproj` and
> `tests/TourSearch.IntegrationTests/TourSearch.IntegrationTests.csproj` already cite
> `docs/adr/036-test-parallelization-policy.md`, so the id and filename are fixed by existing text.
> Choosing a policy here, inside a documentation-backfill task, would be inventing a decision —
> `AGENTS.md` §5 forbids it.

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

**None — this ADR is `Proposed`.** The decision is deferred to a dedicated task.

Recorded, for that future decision:

- **Option A — parallelize at method level.** `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`.
  `[FACT]` verified by reproduction: this silences `MSTEST0001`.
- **Option B — do not parallelize.** `[assembly: DoNotParallelize]`.
  `[FACT]` verified by reproduction: this also silences `MSTEST0001`.
- The two options are not mutually exclusive across projects: the unit project and the integration
  project may legitimately choose differently, given the shared-database constraint above.
- A third, unverified possibility — parallelizing integration tests with per-class or per-test databases
  — is **not** covered by this ADR and would need its own analysis of the Testcontainers setup.

## Consequences

- The dangling reference is resolved: both `.csproj` comments now point at an existing file.
- The `[PLANNED]`/`[OPEN]` state of this policy is explicit rather than implied by a suppression whose
  reason lived only in an XML comment.
- **The interim suppression stays.** Removing it before the policy is decided would break the build with
  `MSTEST0001`, since both projects have no test file in which to declare the attribute.
- The first real test file added to either project **must** resolve this for its project, and must
  replace `Proposed` here with `Accepted`, naming the chosen option and the reason. Until then, a green
  `dotnet test` proves only that the tooling is wired — see `docs/conventions.md#testing`.
- The earlier references in both `.csproj` files to "open question 12" in `docs/open-questions.md` are
  **wrong**: that file has 11 questions. Either the numbering must be extended with this question, or
  those comments must point at this ADR instead. Tracked in the task outcome.
