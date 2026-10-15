# Development conventions

This file is the single source of truth for rules. If [agents.md](./agents.md) contradicts it, this file wins.

## General

- **C#** — the language level supported by .NET 10.
- `<Nullable>enable</Nullable>` in all projects.
- `ImplicitUsings` enabled.
- `async`/`await` wherever there is I/O.
- `TreatWarningsAsErrors` on.

## DI

- Registration via extension methods: `AddInfrastructure()`, `AddScraper()`.
- Do not resolve dependencies manually through `IServiceProvider` without a reason.
- **Lifetimes**:
  - `Scoped` — **default**. Each subsystem gets its own graph; no accidental sharing.
  - `Singleton` — only for services that must be **one shared instance across the whole graph**: stateful resources such as the DB connection pool, the Playwright browser pool, and the `BackgroundService` itself.
  - `[FACT]` the previous version had this inverted ("`Singleton` only for stateless"). Stateless services are cheap to resolve repeatedly — `Scoped` is exactly right for them. `Singleton` is for shared **state**, and using it for stateless helpers is harmless but pointless.
- **`BackgroundService` lifecycle** `[DECIDED]`: registered in the **Worker** graph only, resolved at startup, and its loop holds the process `CancellationToken`. The API graph does not contain it. See [runtime.md](./runtime.md#startup).

## Cancellation

- `CancellationToken` is propagated through every layer: job loop → page navigation → parser → DB writes.
- Check `ctc.IsCancelled` at loop boundaries, not only at the top.
- Never `catch { }` around cancellation; rethrow.
- Never block shutdown with `.Result` / `Wait()`; use `AsyncMain` and `await`.
- Cancellation means "stop and release the lease", never "finish the job anyway".

## Error handling

- Do not swallow exceptions.
- API: `ProblemDetails` for HTTP-level problems.
- Worker: translate into a job state — `ErrorCode` + `ErrorMessage` + `Retryable` from the taxonomy in [domain.md](./domain.md#error-taxonomy).
- Polly for transient network errors only.
- Every log line and every job error carries a **correlation id** (`job_id` where applicable, otherwise a generated one) so a failure can be traced end to end.

## Logging

- Only `ILogger<T>`. No `Console.WriteLine`.
- Structured: `_logger.LogInformation("Job {JobId} completed", jobId)`.
- No PII, no secrets.
- Serilog as sink (file + console; later Seq/ELK).
- Levels: `error` = needs attention now; `warn` = degraded but handled (`Degraded`, retries); `info` = job lifecycle milestones; `debug` = per-page detail, off in production.

## Configuration

- `appsettings.json` — defaults only, **no secret fields**.
- `appsettings.{Environment}.json` — per-environment non-secret overrides.
- User Secrets (`LocalSecrets.env` at the repo root, or `*.LocalSecrets.env`) — local development. Both patterns are in `.gitignore`; a bare `*` pattern does not cover the bare filename, so both are listed.
- Env vars — production.
- Never commit secrets, and never put a literal password into a committed example — even a fake one teaches a policy-violating pattern.

## Testing

- **Unit tests** — domain logic + parser against fixture HTML.
- **Integration tests** — API + DB via **Testcontainers (PostgreSQL)**.
- **No test may contact TourVisor.ru.**
- Naming: `MethodName_Scenario_ExpectedResult`.
- **Parallelization is declared explicitly, never suppressed.** `[FACT]` MSTest 4.0.2 makes `MSTEST0001` an error ("declare the parallelization policy"), and `TreatWarningsAsErrors` promotes it to a build failure. Declare it as an assembly attribute in a real test file: `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` for state-free suites, `[assembly: DoNotParallelize]` when tests share a resource. Do **not** silence it with `NoWarn` — see [ADR 036](./adr/036-test-parallelization-policy.md).
  - Currently: `TourSearch.UnitTests` declares method-level parallelization. `TourSearch.IntegrationTests` still carries an interim `NoWarn` because it has no test file yet and shares one Testcontainers instance; that is tracked debt in [open-questions.md](./open-questions.md) question 12.
- **A green test run proves nothing if zero tests ran.** `dotnet test` exits `0` while reporting "no tests available". Check the executed count before treating a run as evidence.
- Parser regression tests use committed "known good" snapshots under `tests/fixtures/` (a deliberate, size-capped subset — not the runtime snapshot directory).

## Git

- Branches: `main`, `feature/*`, `fix/*`.
- Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `adr:`.
- PR required, minimum 1 approve.
- CI on every PR: `dotnet build` + `dotnet test`. `[PLANNED]` — the repo has no CI config yet; do not describe it as existing.
- `[FACT]` the solution builds with warnings as errors today, but two spellings in [AGENTS.md §4](../AGENTS.md) are **not real CLI options** in the .NET 10 SDK — `dotnet build --all-warnings-as-errors` and `dotnet test --summary=brief` are both rejected with `MSB1001`. The guarantee is enforced instead by `TreatWarningsAsErrors` in `Directory.Build.props`, which covers every project through the sln. The working loop is `dotnet build TourSearch.sln` + `dotnet test TourSearch.sln --verbosity minimal`; do not copy invented flags into scripts.
- `[FACT]` `dotnet test` currently reports "no tests available" for both test projects and still exits `0` — the projects contain no `.cs` files yet. A green test run is therefore **not** evidence of coverage; it only proves the tooling is wired. The first real test must also resolve its parallelization policy (see the interim `MSTEST0001` suppression in both test csproj files).

## Code analysis

- `.editorconfig` at the root — `[FACT]` present (untracked as of 2026-10-13; see [Repo state](./open-questions.md#repo-state)).
- `TreatWarningsAsErrors` on.
- StyleCop / Roslyn analyzers where practical.

## Documentation rules

- Every doc uses the status legend from [agents.md](./agents.md#status-legend). A plan must not be written as a fact.
- An external claim that matters (version, EOL date, library API, spec detail) gets a `[FACT]` marker and must be verifiable against a source. A half-remembered warning gets marked `[UNVERIFIED]` and is reproduced before it is trusted — see [database.md](./database.md).
- Contradictions between docs are defects and must be resolved in favour of one of them, not left to the reader.

## ADRs

- Every non-obvious decision gets a file `docs/adr/NNN-title.md`: context, decision, consequences, status.
- `[FACT]` the directory **exists** and holds the backfill of every decision previously marked `[DECIDED]` — see the index at [docs/adr/README.md](./adr/README.md). The one exception to "decided" is [ADR 036](./adr/036-test-parallelization-policy.md), which is `Proposed` and records an open question on purpose.
- `[FACT]` the numbering scheme is **3 digits**: `docs/adr/036-test-parallelization-policy.md` is cited from `tests/TourSearch.UnitTests/TourSearch.UnitTests.csproj` and `tests/TourSearch.IntegrationTests/TourSearch.IntegrationTests.csproj`. Ids `016`–`035` are deliberately left free; `036` is reserved, not "next". Never renumber — ids are referenced from docs, csproj comments and commits.
- A change that contradicts an ADR requires a new ADR that supersedes it — not a silent edit.

## License

`[OPEN]` — no license file in the repo. Must be chosen before the first public release.
