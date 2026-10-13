# TourSearch

Backend service for tour-offer search, exposed to external AI agents via **MCP (Model Context Protocol)**. It scrapes TourVisor.ru in the background and stores results in PostgreSQL.

**Read [docs/agents.md](./docs/agents.md) first** — it is the entry point and defines the status legend used across all documents.

| | |
|---|---|
| Stack | .NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core 10 + PostgreSQL, Playwright for .NET |
| Processes | `TourSearch.Api` (MCP endpoint) + `TourSearch.Worker` (scraping) |
| Status | **Docs only** — no sources in this repository yet |

## Layout

```
docs/     # specification and design decisions
```

## Note

Everything in the docs is a plan until the corresponding artifact exists in the tree. See the status legend in [docs/agents.md](./docs/agents.md).
