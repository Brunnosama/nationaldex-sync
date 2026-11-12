# 0001. Manual PokeAPI import with versioned snapshots

- Status: accepted
- Date: 2026-11-12

## Context

All Pokémon data (species, forms, games, availability) comes from PokeAPI. The app must keep working when PokeAPI is slow, down, or changes its data, and every change to the data users see has to be reviewable and tested like code.

## Decision

- Only a manual .NET console command in `tools/import` calls PokeAPI, and only on a developer machine.
- Each run writes a new, immutable snapshot `data/snapshots/vN` (JSON, one record per line) with a `manifest.json` (counts and file hashes) and a `DIFF.md` against the previous version.
- A real import refuses to run outside a `data/*` branch. The snapshot goes through a pull request: the diff report is reviewed, CI runs the snapshot validation tests, and only a merge releases it.
- Production and the API never call PokeAPI; they load released snapshots. The nightly contract test (Phase 6) is the only other PokeAPI caller, and its job is to tell us when a new import is needed.
- The import is polite: one request at a time with a minimum gap, retries with back-off, and an on-disk response cache so re-runs and dry runs cost nothing.
- The import checks PokeAPI's response format (all used fields are `required` in the models) and stops with a clear error instead of producing a snapshot from changed data.

## Consequences

- Data changes are visible in git history and in a PR diff, and can be tested before release.
- PokeAPI outages never affect users.
- Data is only as fresh as the last import; that is intended.
- The repository grows with each snapshot version. If that becomes a problem, older versions can move to release assets.
