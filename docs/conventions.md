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
- User Secrets (`*.LocalSecrets.env`) — local development.
- Env vars — production.
- Never commit secrets, and never put a literal password into a committed example — even a fake one teaches a policy-violating pattern.

## Testing

- **Unit tests** — domain logic + parser against fixture HTML.
- **Integration tests** — API + DB via **Testcontainers (PostgreSQL)**.
- **No test may contact TourVisor.ru.**
- Naming: `MethodName_Scenario_ExpectedResult`.
- Parser regression tests use committed "known good" snapshots under `tests/fixtures/` (a deliberate, size-capped subset — not the runtime snapshot directory).

## Git

- Branches: `main`, `feature/*`, `fix/*`.
- Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `adr:`.
- PR required, minimum 1 approve.
- CI on every PR: `dotnet build` + `dotnet test`. `[PLANNED]` — the repo has no CI config yet; do not describe it as existing.

## Code analysis

- `.editorconfig` at the root — `[PLANNED]`, not present yet.
- `TreatWarningsAsErrors` on.
- StyleCop / Roslyn analyzers where practical.

## Documentation rules

- Every doc uses the status legend from [agents.md](./agents.md#status-legend). A plan must not be written as a fact.
- An external claim that matters (version, EOL date, library API, spec detail) gets a `[FACT]` marker and must be verifiable against a source. A half-remembered warning gets marked `[UNVERIFIED]` and is reproduced before it is trusted — see [database.md](./database.md).
- Contradictions between docs are defects and must be resolved in favour of one of them, not left to the reader.

## ADRs

- Every non-obvious decision gets a file `docs/adr/NNN-title.md`: context, decision, consequences, status.
- `[PLANNED]` — the `docs/adr/` directory does not exist yet; the decisions already marked `[DECIDED]` in these docs must be written up there before implementation starts.
- A change that contradicts an ADR requires a new ADR that supersedes it — not a silent edit.

## License

`[OPEN]` — no license file in the repo. Must be chosen before the first public release.
