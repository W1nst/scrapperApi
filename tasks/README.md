# tasks/

Work queue for this repository. **Tracked in git** — these files are artifacts, not scratch space.

## Contract

| | |
|---|---|
| Producer | the `task-creator` skill (`/skill:task-creator`) |
| Consumer | the `task-solver` skill (`/skill:task-solver`) |
| Ready path | `tasks/queue/NNN-<slug>.md` |
| Finished path | `tasks/done/NNN-<slug>.md` |
| Naming | `NNN-<slug>.md` — zero-padded sequential id, lowercase-hyphenated ASCII slug |
| Ready state | header field `Status: ready` |
| Blocked state | header field `Status: blocked`, file **stays** in `tasks/queue/` |
| Finished state | file moved to `tasks/done/`, `Status` updated to `done`, `## Outcome` appended |

## Layout

```
tasks/
├── README.md          # this file
├── queue/             # todo — ready and blocked work
│   └── NNN-slug.md
└── done/              # finished work, each with an appended `## Outcome`
    └── NNN-slug.md
```

`queue/` and `done/` are both exactly **one level below `tasks/`**, at the same depth. That is
deliberate: moving a file between them does **not** change how its relative links resolve.

## Lifecycle

```
/skill:task-creator  →  tasks/queue/NNN-slug.md  (Status: ready)
                              │
/skill:task-solver <file>      │
         │  implement + verify locally
         ├─→  fails   →  tasks/queue/NNN-slug.md  (Status: blocked — stays here)
         └─→  accepted → tasks/done/NNN-slug.md   (Status: done + ## Outcome)
```

Moving `tasks/queue/NNN.md` → `tasks/done/NNN.md` is a **same-depth rename**. No link rewriting is
needed, and none is expected.

## Rules

- `NNN` is the next free number across **both** `queue/` and `done/`. Never reuse a number, and never
  renumber an existing task — ids are referenced from ADRs and commit messages.
- A task file is written only after its author confirmed the specification. Do not commit a
  half-interviewed draft.
- One task = one reviewable unit of work. If a task needs a migration **and** a new MCP tool **and** a
  scraper change, it is probably two tasks.
- `task-solver` owns the terminal edit of a task file: it sets `Status` (`done` or `blocked`) and appends
  a final `## Outcome` (or `## Blocked`) block recording the date, the commit, which acceptance criteria
  were verified, and what was left open. That is the **only** permitted in-place edit — the body of the
  specification, the requirements and their criteria are never rewritten to match what was actually
  built. If a criterion was wrong, the task is blocked and amended by `task-creator`, not quietly
  restated after the fact.
- `task-solver` implements exactly one named task. It does not pick work from the queue on its own.
- A failed task is a legitimate end state: leave it in `tasks/queue/` with `Status: blocked` and the
  evidence in `## Blocked`. Never mark a task `done` to clear the queue.
- Never put a secret, a real credential, or a live-site call into a task file — the rules in
  [`docs/conventions.md`](../docs/conventions.md) apply to this directory too.

## Relative links in a task file

Write them once, for **depth 2** (`tasks/queue/`), and they keep working after the move to `done/`:

| Target | Correct link from `tasks/queue/` **and** `tasks/done/` | Wrong |
|---|---|---|
| `AGENTS.md` | `../../AGENTS.md` | `../AGENTS.md` |
| `docs/x.md` | `../../docs/x.md` | `../docs/x.md` |
| `docs/adr/NNN-y.md` | `../../docs/adr/NNN-y.md` | `../docs/adr/NNN-y.md` |
| `tasks/README.md` | `../README.md` | `README.md` |
| a sibling task | `../queue/NNN-x.md` or `../done/NNN-x.md` | `NNN-x.md` |

Because both folders sit at the same depth, a task file must **never** be authored with a link that
assumes the other folder's depth — there is no longer any such difference to get wrong.

The one link that legitimately changes is to a *sibling*: if a task links another task by its folder
(`../queue/NNN-x.md`) and that task later moves to `done/`, the link must follow. Prefer referencing a
sibling by id in prose (`task 002`) when the target may move; link the file only when it has already
been closed.

Links must be **verified, not assumed**: check that each target exists (`test -f`) and that each
`#anchor` exists in the target file. A link that was already broken stays broken after any mechanical
rewrite — rewriting is not validation.

A broken link in a closed task is a defect worth fixing in place, because the file is the artifact a
future reader trusts.
