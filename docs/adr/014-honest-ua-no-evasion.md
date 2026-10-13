# ADR 014 — Honest UA, no evasion by default

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/scraping.md#the-contradiction-that-had-to-be-resolved`](../scraping.md#the-contradiction-that-had-to-be-resolved), [`docs/scraping.md#anti-bot`](../scraping.md#anti-bot)

## Context

The specification previously said "honest User-Agent" **and** asked "do we need User-Agent rotation and
a proxy pool?" in the open questions. Those two are in direct conflict: an honest UA plus rotation plus
proxies is an **evasion** strategy.

Evasion is not a technical decision — it is a legal and compliance one. Treating it as an engineering
toggle is how a compliant scraper becomes a non-compliant one without anyone deciding to.

## Decision

The **default mode** is an honest User-Agent, obeying `robots.txt` and obeying throttling. No UA
rotation, no proxy pool.

- If the source blocks us, the job goes `Failed` with `ANTI_BOT_DETECTED` and `Retryable = false` — we do
  **not** silently escalate.
- Evasion (UA rotation, proxy pool, CAPTCHA solving) is `[OPEN]` and **out of scope until there is
  explicit legal review and a written ADR**. It must not be implemented "because it seems necessary".

## Consequences

- A block is a visible, terminal failure rather than an arms race. `ANTI_BOT_DETECTED` is
  `Retryable = false` ([ADR 007](./007-error-taxonomy-retryable.md)), so the queue does not burn itself
  retrying against a wall.
- Being blocked is a business signal, not a bug: it means the arrangement with the source needs
  renegotiating. Escalating silently would hide that.
- The polite default also bounds our footprint: one browser per worker process
  ([`docs/runtime.md#playwright-footprint`](../runtime.md#playwright-footprint)), throttled navigation.
- Adopting evasion later requires a **new** ADR that supersedes this one, plus legal review — never a
  silent edit, and never a config flag flipped in production.
- `AGENTS.md` §8 lists anti-bot evasion as out of scope, and §3 makes it a hard rule requiring legal
  review plus an ADR. This is the ADR that records the default those rules refer to.
