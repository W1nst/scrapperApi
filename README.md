# TourSearch

Backend service for tour-offer search, exposed to external AI agents via **MCP (Model Context Protocol)**. It scrapes TourVisor.ru in the background and stores results in PostgreSQL.

**Read [docs/agents.md](./docs/agents.md) first** — it is the entry point and defines the status legend used across all documents.

| | |
|---|---|
| Stack | .NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core 10 + PostgreSQL, Playwright for .NET |
| Processes | `TourSearch.Api` (MCP endpoint) + `TourSearch.Worker` (scraping) |
| Status | **Skeleton.** `docs/` and `AGENTS.md` are tracked; `.editorconfig`, `Directory.Build.props`, `TourSearch.sln`, `src/` and `tests/` exist but are **untracked**. No `docs/adr/`, no CI, no license, no implementation — the two `Program.cs` files are placeholders that exit `1`. See [Repo state](./docs/open-questions.md#repo-state). |

## Layout

```
docs/                    # specification and design decisions
AGENTS.md                # operational instructions for the agent that implements this app
.gitignore                # tracked; policy source is docs/architecture.md + docs/scraping.md
tasks/                   # work queue for the task-creator/task-solver skills; see tasks/README.md
Directory.Build.props    # net10.0, Nullable, TreatWarningsAsErrors — applies to every project
TourSearch.sln           # 6 src projects + 2 test projects
src/                     # TourSearch.{Api,Core,Infrastructure,Infrastructure.Migration,Scraper,Worker}
tests/                   # TourSearch.{UnitTests,IntegrationTests} + fixtures/
```

`docs/`, `AGENTS.md` and `.gitignore` are the only **tracked** files listed above; `tasks/`, `Directory.Build.props`, `TourSearch.sln`, `src/` and `tests/` exist in the working tree but are uncommitted. Verify with `git status` before relying on any of it.

## Workflow

Work is specified and executed by two project-aware Pi skills:

```
/skill:task-creator <краткое описание>   →  tasks/queue/NNN-slug.md  (Status: ready)
/skill:task-solver  tasks/queue/NNN-slug.md  →  code + local verification + report
                                          →  tasks/done/NNN-slug.md  (Status: done)
```

`task-creator` interviews you and writes the specification; it never invents a task and never writes
before you confirm. `task-solver` implements **one** named task, checks every acceptance criterion
with real command output, and moves the file only after you accept the result. A task that fails its
criteria stays in `tasks/queue/` as `Status: blocked`. The queue contract is in [tasks/README.md](./tasks/README.md).

## Note

Everything in the docs is a plan until the corresponding artifact exists in the tree. See the status legend in [docs/agents.md](./docs/agents.md). An artifact that exists but is not yet tracked is neither `[PLANNED]` nor established — do not cite it as a fact.
