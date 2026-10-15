# ADR 049 — The parameter hash is built from an explicit canonical form

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/domain.md#identity-and-dedupe`](../domain.md#identity-and-dedupe)
- **Related:** [ADR 012](./012-no-idempotency-key.md), [ADR 047](./047-reuse-window-separate-from-ttl.md)

## Context

`docs/domain.md#identity-and-dedupe` specifies `ParametersHash` as *"SHA-256 over canonical JSON of
`SearchParameters`: sorted keys, normalized region, ISO-8601 dates, explicit currency, no locale-dependent
number formatting"*. That is a description of a canonical form, and the obvious implementation — serialise
the entity with the default `JsonSerializer` and hash the bytes — **does not produce one**:

- **Property order is not guaranteed.** The default serialiser emits CLR property order, which is an
  implementation detail. A refactor that reorders two properties, or a different EF/JSON version, changes
  every hash.
- **Formatting is culture-dependent.** Number and date formatting follow `CultureInfo.CurrentCulture`, so
  the same request can hash differently on a machine with a different locale.
- **EF's JSONB mapping is PascalCase.** `[FACT]` the stored `parameters` column contains
  `{"Adults": 2, ...}`, while the MCP request uses `adults`. Hashing the stored representation would bind
  the hash to EF's naming, coupling an external contract to an internal mapping detail.

None of these failures is visible in review: the code compiles, the tests pass, and the hash looks fine.
The defect appears as **dedupe silently not working** — distinct hashes for identical requests, so every
request creates a new job instead of reusing one. That is a load and cost bug with no error message.

## Decision

The canonical form is **built explicitly**, field by field, from an ordinal-sorted collection — never by
serialising the entity with a default serialiser.

- Keys sorted with **ordinal** comparison; numbers and dates formatted with `InvariantCulture`.
- Dates as `yyyy-MM-dd` — `start_date` contributes a date, never a time.
- Absent optional bounds contribute **JSON `null`**, so `price_min` absent and `price_min: 0` hash
  differently. They are genuinely different requests: `0` is a bound the caller asked for, absent is not.
- `CurrencyCode` is included, although it lives on `SearchJob` and not on `SearchParameters`
  (`SearchParameters` keeps exactly 8 fields; verified, and no field was added).
- Output format: `sha256:` + lowercase hex, 71 characters — matching the documented prefix and the column's
  configured width.

A test asserts equality across shuffled key order and across `tr-TR` / `en-US` / `ru-RU` cultures, and that
changing any one of the nine parameters changes the hash.

## Consequences

- Dedupe works across processes, machines and locales, which is the property `docs/domain.md` describes and
  which a default serialiser would have quietly broken.
- **Changing the canonical form is a breaking change with a delayed effect.** Every stored hash is
  invalidated, and the symptom is a wave of duplicate jobs rather than an error: `parameters_hash` lookups
  simply stop matching. Any change to the form needs a new ADR and a plan for the existing rows — the same
  class of change as the `idempotency_key` decision in ADR 012.
- The form is now explicit code that a reviewer can read and compare against the doc sentence, instead of
  being an emergent property of a serialiser.
- The stored `parameters` JSONB stays PascalCase (EF's mapping, unchanged). The hash deliberately does not
  use it, so EF's naming can change without touching the dedupe key.
- Nullable optional numerics (`PriceMin`, `PriceMax`) contribute `null`; non-nullable ones are unaffected.
  `HotelStars` stays a non-nullable `int` where `0` already means "any" — making it nullable would have
  created a second, ambiguous spelling of "no preference".
