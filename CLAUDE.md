# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**`HANDOFF.md` is the source of truth** for goals, business rules, stack, repo layout and the phased plan (Phases 1–9, each with "Done when" criteria). Read the relevant section before starting any task. Phase 1 (PokeAPI import, snapshot `v1`, diff report, snapshot validation tests, PR workflow) is in place; most other folders are placeholder READMEs naming the phase that fills them. Decisions are recorded in `docs/adr/`.

When you add a project (app, API, tool, test suite), add its commands here, including how to run a single test.

## Commands

Run from the repository root unless noted.

```sh
# PokeAPI import (.NET 10). Real imports only on a data/* branch; --dry-run writes nothing.
dotnet run --project tools/import/src/PokedexSync.Import -- import --dry-run
dotnet run --project tools/import/src/PokedexSync.Import -- import --dry-run --species-limit 30   # quick check
dotnet run --project tools/import/src/PokedexSync.Import -- import                               # on data/pokeapi-vN
dotnet run --project tools/import/src/PokedexSync.Import -- diff data/snapshots/v1 data/snapshots/v2

# Importer unit tests (xUnit, no network)
dotnet test tools/import/Import.slnx
dotnet test tools/import/Import.slnx --filter "FullyQualifiedName~SnapshotBuilderTests.Battle_only_forms_are_left_out"

# Snapshot validation tests (Vitest), lint and format, from tests/
cd tests && npm ci
npm run test:data
SNAPSHOT_VERSION=v1 npm run test:data
npx vitest run --project data -t "Unown"
npm run typecheck && npm run lint && npm run format:check
```

CI: `.github/workflows/pr-checks.yml` runs both test suites plus typecheck/lint/format on every PR.

## Phase 1 data pipeline

- `tools/import` fetches PokeAPI (paced, retried, cached in git-ignored `tools/import/.cache/`), deserializes into DTOs whose fields are all `required` (a missing or retyped field → `PokeApiFormatException`, exit code 2), then `SnapshotBuilder` (pure) applies the entry rules and `SnapshotStore` writes `data/snapshots/vN` (one JSON record per line + `manifest.json` with SHA-256 per file) and `SnapshotDiff` writes `DIFF.md`.
- `tests/data` validates snapshots by reading the JSON directly, deliberately sharing no code with the importer. If you change the snapshot shape, update `SnapshotModels.cs`, `tests/data/snapshot.ts` and ADR 0002 together.
- Never edit snapshot files by hand: the manifest hashes will fail validation. Change importer code and re-import on the data branch.

## What this is

A QA portfolio project: an offline-first Angular PWA that tracks a National Pokédex collection across every game, syncing with a .NET GraphQL API. **The test suite and CI pipelines are the real deliverable**; the app exists to be tested. The owner wants each step explained briefly so they can describe it in interviews.

## Hard constraints (do not violate)

- No paid services; every tool must be free / open source / free tier.
- Containers run on **Podman only** (`podman compose`; on Windows via the Podman WSL machine). Never Docker. Kind uses `KIND_EXPERIMENTAL_PROVIDER=podman`.
- **PokeAPI is called only by the manual import command in development** (`tools/import`), with polite request pacing and local caching. Production, the API and the app never call PokeAPI. The nightly contract test in CI is the only other exception.
- Pokémon data reaches users only through a release: import on its own `data/pokeapi-vN` branch → versioned snapshot in `/data/snapshots/vN` + diff report → PR review + CI → merge → release pipeline loads it into the DB.
- PokeAPI is the only data source.
- Nothing is tested in production; production is checked only via logs/traces and read-only SQL (`/ops/queries`).
- k6 and Bruno rate-limit tests target only this project's API in dev or CI.
- No secrets in git; test users and keys come from seed files and `.env.example`.

## Architecture (planned)

```
Angular PWA + RxDB (Dexie/IndexedDB) ──(RxDB replication: GraphQL + WebSocket)──► .NET API (Hot Chocolate) ──► SQL Server
                                                                                   ├──► Keycloak (OIDC)
                                                                                   └──► OpenTelemetry → Grafana/Loki/Tempo
```

- **Two replicated datasets:** Pokémon data (read-only, delta by data version) and per-user progress (bidirectional, private to the user).
- **Offline-first:** the PWA writes progress to RxDB first; replication pushes/pulls on reconnect; the server pushes to the user's other devices over WebSocket.
- **Conflict rule:** per-field merge. Same field changed on two devices → later client timestamp wins, and the losing value goes to a user-visible conflict log.
- **Data version changes:** progress for an entry removed in a new version is kept and flagged, never deleted.
- **Validation runs twice:** in the app before saving, and again in the API against the Pokémon data it holds (including that a user writes only their own records).
- **Public demo** (GitHub Pages) is frontend-only, local-only mode: the released snapshot as static files, progress on the device, JSON export/import.

## Domain rules that shape code and tests

Full rules are in `HANDOFF.md` §3 (to be copied to `docs/BUSINESS-RULES.md`). The ones easy to get wrong:

- A **dex entry** = species × form × gender variant × shininess. Forms are PokeAPI permanent forms (`is_battle_only: false`), regional forms included. Every entry has a normal and a shiny version.
- **Gender split** needs all of: species `has_gender_differences`, a female HOME sprite for that form, and a form name without a `male`/`female` part (Meowstic-Male is already a gender). See ADR 0002.
- **Entry IDs must be deterministic** across imports: `{speciesId}-{formId}-{m|f|x}-{n|s}` (e.g. `25-25-f-s`), so user progress stays attached across data versions.
- **Availability** comes from PokeAPI encounters, which are per Pokémon: every permanent form of that Pokémon shares them, except in games older than the form's introduction generation.
- **"Game obtained in"** must be from the entry's introduction generation or later; the same rule applies to every variant. It is independent of availability, so an entry can be recorded in a game where PokeAPI lists no encounter.
- **Gen 3 Deoxys rule:** obtaining Deoxys in a Gen 3 game forces the form (FireRed→Attack, LeafGreen→Defense, Emerald→Speed, Ruby/Sapphire/Colosseum/XD→Normal). From Gen 4 on, the user picks. Enforced in both the app and the API.
- Unchecking "Obtained" hides optional details but keeps them; re-checking restores them.
- Snapshot validation expectations: 28 Unown forms, 4 Deoxys forms, gender pairs only where flagged, a shiny for every entry, an introduction generation on every entry, unique entry IDs, and every availability row pointing to an existing entry and game.

## Conventions

- TypeScript strict mode; ESLint + Prettier in app and tests.
- `data-testid` on every interactive element (E2E and AI-generated tests rely on them).
- Conventional commits; one branch per task; PRs must pass CI. PokeAPI imports always go on their own `data/` branch.
- Write an ADR in `docs/adr` for each significant choice (snapshot import, entry model/IDs, Deoxys rule, conflict rule, environments, Podman-only, sharding, Keycloak).
- Follow the repo layout in `HANDOFF.md` §8 when creating directories.
