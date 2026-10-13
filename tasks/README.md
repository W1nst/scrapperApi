# tasks/

Work queue for this repository. **Tracked in git** — these files are artifacts, not scratch space.

## Contract

| | |
|---|---|
| Producer | the `task-creator` skill (`/skill:task-creator`) |
| Consumer | the `task-solver` skill (`/skill:task-solver`) |
| Naming | `NNN-<slug>.md` — zero-padded sequential id, lowercase-hyphenated ASCII slug |
| Ready state | header field `Status: ready` |
| Blocked state | header field `Status: blocked`, file stays in `tasks/` |
| Finished state | file moved to `tasks/done/`, `Status` updated to `done`, `## Outcome` appended |

## Rules

- `NNN` is the next free number across **both** `tasks/` and `tasks/done/`. Never reuse a number,
  and never renumber an existing task — ids are referenced from ADRs and commit messages.
- A task file is written only after its author confirmed the specification. Do not commit a
  half-interviewed draft.
- One task = one reviewable unit of work. If a task needs a migration **and** a new MCP tool
  **and** a scraper change, it is probably two tasks.
- `task-solver` owns the terminal edit of a task file: it sets `Status` (`done` or `blocked`) and appends
  a final `## Outcome` (or `## Blocked`) block recording the date, the commit, which acceptance
  criteria were verified, and what was left open. That is the **only** permitted in-place edit — the
  body of the specification, the requirements and their criteria are never rewritten to match what
  was actually built. If the criterion was wrong, the task is blocked and amended by `task-creator`,
  not quietly restated after the fact.
- **Relative links depend on the file's depth, and the depth changes when it is closed.** A file in
  `tasks/` reaches `AGENTS.md` as `../AGENTS.md`; after the move to `tasks/done/` the same target is
  `../../AGENTS.md`. So:
  - `task-creator` writes links for **`tasks/`** (`../AGENTS.md`, `../docs/...`) — not for `done/`.
  - `task-solver` rewrites every outbound link to add one level **during the move**, and re-verifies
    that each link, anchor, and in-`tasks/` target resolves from the new location.
  - A link that was already wrong stays wrong after a mechanical rewrite. Rewriting is not validation:
    check that the target exists.
- `task-solver` implements exactly one named task. It does not pick work from the queue on its own.
- A failed task is a legitimate end state: leave it in `tasks/` with `Status: blocked` and the
  evidence in `## Blocked`. Never mark a task `done` to clear the queue.
- Never put a secret, a real credential, or a live-site call into a task file — the rules in
  [`docs/conventions.md`](../docs/conventions.md) apply to this directory too.

## Layout

```
tasks/
├── README.md          # this file
├── .gitkeep
├── NNN-slug.md        # ready / blocked work
└── done/
    ├── .gitkeep
    └── NNN-slug.md    # completed work, with an appended `## Outcome`
```

## Lifecycle

```
/skill:task-creator  →  tasks/NNN-slug.md  (Status: ready)
                              │
/skill:task-solver <file>      │
         │  implement + verify locally
         ├─→  fails   →  tasks/NNN-slug.md  (Status: blocked — stays here)
         └─→  accepted → tasks/done/NNN-slug.md  (Status: done + ## Outcome)
```
