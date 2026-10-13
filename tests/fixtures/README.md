# tests/fixtures/ — parser fixtures

Policy source: [docs/conventions.md](../docs/conventions.md#testing) and
[docs/scraping.md](../docs/scraping.md).

- Parser regression tests read **committed, known-good HTML snapshots** from this
  directory. They never contact TourVisor.ru — no test and no CI run may touch the
  live site.
- Fixtures are the raw response body plus a small sidecar describing the expected
  parse, so a fixture failure points at the selector, not at the site.
- Real scraped snapshots belong to `TOURSEARCH_SNAPSHOT_DIR` **outside** the repo
  (third-party content, unbounded, TTL-managed — see `.gitignore`: `**/snapshots/**`).
  Only deliberately curated, size-capped fixtures are committed here.
- Fixture directory names are kept out of the reach of the Playwright ignore rules
  (`.gitignore` deliberately has no bare `**/chromium-*/` pattern for this reason).

There are no fixtures here yet — writing them is part of step 9 in AGENTS.md §2
("TourSearch.Scraper — Playwright + selectors.json: needs fixtures first"), and
`selectors.json` itself needs a schema and a version field
([docs/scraping.md](../docs/scraping.md)).
