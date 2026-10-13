# ADR 015 — `Singleton` is for shared state, not for stateless helpers

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/conventions.md#di`](../conventions.md#di)

## Context

The dependency-injection lifetime rule was previously inverted: the doc said "`Singleton` only for
**stateless** services". That is backwards, and it produces two problems:

- It encourages `Singleton` registration of stateless helpers, which is harmless but pointless — they
  are cheap to construct, so sharing them buys nothing.
- It implies that stateful services (a DB pool, a browser pool, a hosted service) must be `Scoped`, which
  is wrong: they must be **one shared instance across the whole graph**, otherwise each scope opens its
  own pool.

## Decision

- **`Scoped` is the default.** Each subsystem gets its own graph; nothing is shared accidentally.
- **`Singleton` is for services that must be one shared instance across the whole graph** — stateful
  resources: the DB connection pool, the Playwright browser pool, and the `BackgroundService` itself.
- Registration goes through extension methods: `AddInfrastructure()`, `AddScraper()`.
- Do not resolve dependencies manually through `IServiceProvider` without a stated reason.

The rule in one line: **`Singleton` is for shared state; `Scoped` is the default for everything else.**

## Consequences

- Stateful resources are shared by construction, so a browser or connection pool is not silently
  duplicated per scope — which would be an RSS and connection-count problem, not a correctness one until
  it exhausts limits.
- Stateless services are re-resolved per scope. That is correct and cheap; nobody needs to prove a
  helper is thread-safe in order to register it.
- `BackgroundService` is `Singleton` **and** lives in the Worker graph only
  ([ADR 004](./004-backgroundservice-started-by-worker-only.md)). The two rules compose: shared state,
  single owner process.
- A `Singleton` that depends on a `Scoped` service is a captive-dependency bug, and the DI container
  will throw on build; the default lifetime keeps that class of error rare.
