# agents.md — entry point

> This file is the single required document. Everything else is reached through links.

## Status legend

| Marker | Meaning |
|---|---|
| `[PLANNED]` | Not in code — the repo currently contains `docs/`, `README.md`, `AGENTS.md` and a tracked `.gitignore`: no sources, no tests, no CI, no `.editorconfig`, no license, no `docs/adr/` |
| `[DECIDED]` | Decision made — must be recorded as an ADR (`docs/adr/NNN-*.md`) |
| `[OPEN]` | Unresolved — see [open-questions.md](./open-questions.md) |
| `[FACT]` | Verified against external docs / version data |

If a doc states a rule but the repo has no such artifact, that rule is `[PLANNED]`. **Do not present plans as facts.**

## What we do

Backend service for **tour offer search**, integrated via **MCP (Model Context Protocol)**.

External AI agent calls our MCP tools:

1. `list_regions` — the only source of valid region ids; the agent must not invent one.
2. `search_tours` — creates a search task, returns `job_id` **immediately** (non-blocking).
3. `get_search_results` — fetches results when they are ready, **paginated**.

Two more tools are specified but not in the mandatory set: `cancel_search` (optional) and `list_recent_searches` (**blocked** until caller identity exists). See [mcp-tools.md](./mcp-tools.md).

In the background the service scrapes **TourVisor.ru**, collects suitable offers, and stores them in PostgreSQL.

## Stack

- **.NET 10** `[FACT]` — released 2025-11-11, release type **LTS**, end of support 2028-11-14. (.NET 8 and .NET 9 reach EOL 2026-11-10 — do not target them.)
- **ASP.NET Core Web API** (Minimal APIs)
- **EF Core 10** + **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- **Playwright for .NET** `[FACT]` — official port `microsoft/playwright-dotnet`. Requires a `playwright.ps1` install step; see [runtime.md](./runtime.md)
- **MCP** — transport: **Streamable HTTP** `[DECIDED]` (remote server; stdio is not applicable)
- **BackgroundService** (DI) — started by **Worker only** `[DECIDED]`, see [runtime.md](./runtime.md)

## Navigation

| File | About |
|---|---|
| [architecture.md](./architecture.md) | Projects, dependency graph, data flow, key decisions |
| [runtime.md](./runtime.md) | Processes, startup/shutdown, Playwright footprint, restart semantics |
| [domain.md](./domain.md) | Entities, fields, statuses, error taxonomy, identity rules |
| [mcp-tools.md](./mcp-tools.md) | MCP server contract: tools, schemas, pagination, error semantics |
| [scraping.md](./scraping.md) | Rules for working with TourVisor.ru |
| [database.md](./database.md) | EF Core, DbContext, migrations, indexes |
| [conventions.md](./conventions.md) | Code style, DI, logging, tests, git, ADRs |
| [open-questions.md](./open-questions.md) | Blocking and non-blocking questions with proposed defaults |

## TL;DR rules for the developer agent

The single source of truth for rules is [conventions.md](./conventions.md). This list is a summary only — if it contradicts conventions, conventions wins.

- **C#**, `Nullable` enabled, `ImplicitUsings` enabled.
- **`async`/`await`** for all I/O; **`CancellationToken`** propagated through every layer.
- **Secrets** — env vars in production, User Secrets locally. Never in committed files, including examples.
- **Never** access TourVisor.ru from CI or unit tests.
- **Migrations** — only via `dotnet ef`, never hand-edited without a reason.
- **Logging** — `ILogger<T>`, structured, no PII, no secrets.
- **Out of scope**: frontend, payment, booking, tourist PII.

## Out of scope (what we do NOT do)

- Frontend — API + MCP only.
- Official TourVisor search API — `[FACT]` none is publicly available for search; scraping is the only option. If an official API appears, it becomes a new `Scraper` implementation behind the same `ITourScraper` interface, not a rewrite.
- Booking and payment.
- Storing personal data of tourists.
- Multi-source aggregation (one source per milestone).
