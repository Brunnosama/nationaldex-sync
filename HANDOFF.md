# Pokédex Sync — Project Handoff

Public QA portfolio project: an offline-first web app (PWA) that tracks a National Pokédex collection across every Pokémon game. Users see each Pokémon and form, the games it appears in and how to obtain it there, and check off what they have on their own profile. The app syncs with its own server on start and works offline after that.

Owner: Brunno Pessoa (QA Automation Engineer). The app exists to be tested; the test suite and pipelines are the real deliverable.

---

## 1. Goals

- A public demo anyone can open (GitHub Pages, local-only mode).
- A full stack that starts with one Podman command, for development, CI and a production-like environment.
- A test suite and CI pipeline that a recruiter or interviewer can read, run and see results for.
- Every tool free: open source, free tier, or free for public repos.

## 2. Hard constraints

- No paid services.
- Containers run on **Podman only**.
- **PokeAPI is only called in development**, by a manual import command. Production never calls PokeAPI.
- Pokémon data reaches users only through a release: imported on a branch, reviewed, tested in CI, merged, then loaded into production.
- **All testing happens in development and CI.** Nothing is tested in production; production is checked only through log analysis and read-only database queries.
- Each user checks and unchecks their own Pokémon. Progress is private to that user.
- k6 and Bruno rate-limit tests target only this project's API in development or CI.
- No secrets in git. Test users and keys come from seed files and `.env.example`.

## 3. What the app does (business rules)

The rules come from the owner's existing personal tracker.

### 3.1 Dex entries (what can be checked)
A **dex entry** is one combination of species, form, gender variant and shininess. There is a single checklist; each Pokémon shows all its entries, and each entry is checked individually.

| Variant | Example | Rule |
| --- | --- | --- |
| Form | Unown-A … Unown-?, Raichu (Alolan) | One per form PokeAPI lists for the species, regional forms included |
| Gender | Venusaur-M, Venusaur-F | Split only for species with visible gender differences (PokeAPI `has_gender_differences`) |
| Shiny | Bulbasaur, Bulbasaur (Shiny) | Every entry has a normal and a shiny version |
| Game-dependent form | Deoxys-N/A/D/S | See 3.4 |

- Forms come from PokeAPI and match what Pokémon HOME stores: the permanent forms (PokeAPI `is_battle_only: false`).
- Entries are grouped by region box (Kanto, Johto, Hoenn, …) and sorted by National Dex number, then form, gender and shininess.
- Entry IDs are built the same way on every import from PokeAPI IDs (species, form, gender) plus shininess, so user progress stays attached to the right entry across data versions.
- Images will come from Pokémon HOME sprites.

### 3.2 Availability (where and how to get it)
For each entry × game, the app shows what PokeAPI reports:

| Field | Source in PokeAPI |
| --- | --- |
| Method | Encounter method (walk, surf, fishing, gift, …) |
| Location | Location area |
| Conditions | Encounter conditions (time of day, swarm, …) and level range |
| Rate | Encounter chance in % |
| Evolves from | Evolution chain, with trigger (level 16, Fire Stone, trade) |

- **PokeAPI is the only source.** If PokeAPI doesn't list an entry for a game, that entry is not shown as part of that game. Nothing is added to fill gaps, and no "missing data" message is shown.
- Every game in PokeAPI's version list is included.
- Version exclusives are flagged (FireRed vs LeafGreen).

### 3.3 Tracking progress (per user)
Each user has a profile. Progress belongs to that profile only; no other user can see or edit it.

Per user × entry, one record:

| Field | Required | Rule |
| --- | --- | --- |
| Obtained | yes | Checkbox |
| Game obtained in | when obtained | One game from the generation the entry's form was introduced in, or any later generation |
| Storage | no | Free text ("HOME box 3") |
| Nature | no | One of the 25 natures |
| IVs | no | Six values 0–31; total calculated |
| Notes | no | Free text, max 500 characters |

- "Game obtained in" follows the introduction generation PokeAPI gives for the species or form, cumulative. The rule is the same for every variant of the entry, gender and shiny included.
- This list is separate from availability (3.2): an entry can be recorded in a game even where PokeAPI lists no encounter for it.
- Unchecking "Obtained" keeps the optional details but hides them; re-checking restores them.
- Views: the full National Dex checklist, and one view per game showing only the entries PokeAPI lists for that game.
- Counters: obtained / total overall, per region box, and per game (out of the entries shown for that game). Each counter can be filtered by variant (shiny only, forms only).

### 3.4 Game-dependent form rule (Gen 3 Deoxys)
In Gen 3 games, Deoxys has no form of its own; the game decides it. When a user marks Deoxys as obtained in a Gen 3 game, the form entry is set automatically:

| Game | Entry |
| --- | --- |
| FireRed | Deoxys-A (Attack) |
| LeafGreen | Deoxys-D (Defense) |
| Emerald | Deoxys-S (Speed) |
| Ruby, Sapphire, Colosseum, XD | Deoxys-N (Normal) |

From Gen 4 on, the user picks the form. All other forms (Unown included) are picked by the user in every game. The Deoxys rule and the "game obtained in" rule are applied in the app and enforced by the API.

### 3.5 Startup sync and offline use
- On start, the app syncs with the server through RxDB replication (GraphQL + WebSocket):
  - **Pokémon data** (read-only): the full entry list, forms, games and availability for every game. The first sync downloads everything; later syncs download only what changed in a new data version.
  - **Progress** (read/write): the user's checklist, both directions.
- After the first sync, the app works fully offline. It shows when it is offline and how many changes are waiting to sync.

## 4. Data lifecycle

```
DEVELOPMENT                                                         PRODUCTION
PokeAPI ──(import command, on a branch)──► snapshot vN in the repo
                                              │  review the diff report
                                              │  adjust app code if needed
                                              │  CI runs every test
                                              ▼
                                       merge ──► release pipeline ──► loads vN into the database
                                                                      apps receive the changes on next sync
```

- **Import command** (`tools/import`): pulls from PokeAPI, builds entries and availability with the rules in section 3, writes a new versioned snapshot to `/data/snapshots/vN`, and prints a diff report against the previous version (new entries, removed entries, changed availability).
- The import checks PokeAPI's response format and stops with a clear error if a field it uses has changed.
- Run only when a developer requests it, always on its own branch (for example `data/pokeapi-v2`), so app code and tests can be updated for the new data before release.
- Data corrections, when needed, are made on that branch as code or snapshot changes, reviewed in the pull request.
- **Seed builder** (`tools/seed`): takes a small fixed slice of the current snapshot plus test users and progress, for local development and E2E only.

## 5. Stack

| Layer | Choice |
| --- | --- |
| Frontend | Angular (latest stable), `@angular/pwa` service worker, TypeScript |
| Local storage | RxDB with Dexie storage (IndexedDB) for Pokémon data and progress |
| Backend | .NET (latest LTS), Hot Chocolate GraphQL, a few REST endpoints, GraphQL subscriptions over WebSocket |
| Rate limiting | ASP.NET Core built-in rate limiter |
| Database | SQL Server Developer edition, container |
| Data tools | .NET console apps: PokeAPI import (development only) and seed builder (local and E2E only) |
| Auth | Keycloak container (OpenID Connect); GitHub as an identity provider inside Keycloak |
| Observability | OpenTelemetry in the API → Grafana, Loki (logs), Tempo (traces) |
| Containers | Podman with `podman compose` (needs a compose provider installed; on Windows, Podman runs in its WSL machine) |
| CI | GitHub Actions (Ubuntu runners have Podman installed) |
| Kubernetes | Kind with the Podman provider (`KIND_EXPERIMENTAL_PROVIDER=podman`) |
| Test management | GitHub Issues + Projects |

## 6. Environments

| Environment | Where | What runs | Tests |
| --- | --- | --- | --- |
| Development | Developer machine, Podman | Full stack with seed data; PokeAPI import available | All |
| CI | GitHub Actions, Podman and Kind | Full stack with seed data or the full snapshot | All, plus nightly PokeAPI contract test |
| Production-like | Owner's machine, separate Podman stack with production settings | Full stack with the released snapshot; no PokeAPI access | None. Checked through Grafana logs and traces and read-only SQL queries |
| Public demo | GitHub Pages | Frontend only, local-only mode: released snapshot as static files, progress on the device, JSON export/import | None |

## 7. Architecture

```
Angular PWA ──(RxDB replication over GraphQL + WebSocket)──► .NET API ──► SQL Server
                                                               ├──► Keycloak (auth)
                                                               └──► OpenTelemetry → Grafana stack
```

### Validation
- The app validates every rule in section 3 before saving.
- The API validates them again against the Pokémon data it holds: required fields, IV range, nature list, note length, entry and game exist, game within the entry's introduction generation or later, Deoxys rule, and that a user only writes their own records.

### Sync and conflicts
- The PWA writes progress to RxDB first, online or offline.
- On reconnect, RxDB pushes changes and pulls server changes; the server pushes updates to the user's other open devices over WebSocket.
- Conflict rule: fields are merged one by one. If two devices changed different fields of the same record, both changes are kept. If they changed the same field, the edit with the later client timestamp wins, and the losing value is saved in a conflict log the user can view.
- Conflict cases to test: same entry checked on device A and unchecked on device B; different fields edited on each device; the same entry marked obtained in two different games.
- Data version changes: progress for an entry that no longer exists in a new data version is kept and flagged, never deleted.

## 8. Repository layout

```
/app              Angular PWA
/api              .NET API (GraphQL, REST, data loader, test reset endpoint)
/tools
  /import         PokeAPI import + diff report (development only)
  /seed           seed builder (local and E2E only)
/data
  /snapshots      versioned Pokémon data (v1, v2, …) with a manifest per version
/infra
  compose.yaml    full stack: db, api, app, keycloak, grafana, loki, tempo
  compose.prod.yaml   production-like settings
  /k8s            Kind cluster config and manifests
/tests
  /e2e            Playwright + TypeScript, Gherkin features (playwright-bdd)
  /cypress        small smoke suite
  /api            Bruno collection (CRUD, validation, permissions, rate limit)
  /perf           k6 scripts (load, stress, spike, soak)
  /data           snapshot validation tests
  /contract       nightly PokeAPI contract test (CI only)
  /fixtures       seeded users and progress
  /specs          Markdown scenario specs for AI generation
/ops
  /queries        read-only SQL checks for the production-like environment
  /dashboards     Grafana dashboards
/ai               test-generation pipeline config (Opencode, Ollama, Playwright MCP)
/docs
  TEST-STRATEGY.md
  test-plan.md
  BUSINESS-RULES.md   section 3 of this file, kept up to date
  DATA-LIFECYCLE.md   section 4 of this file
  /adr            architecture decision records
CLAUDE.md / AGENTS.md   pointers to README, docs and stack commands
README.md
```

## 9. Phases and acceptance criteria

### Phase 1 — Import and first snapshot
- Import command with entry rules (forms, gender variants, shiny variants), the introduction generation of each species and form, and availability for every game.
- First snapshot `v1` committed with its manifest (version, import date, counts per game).
- Diff report format defined (tested with a second dry run).
- Snapshot validation tests: 28 Unown forms, 4 Deoxys forms, gender pairs only where flagged, a shiny version for every entry, only permanent forms, an introduction generation on every entry, unique entry IDs, every availability row points to an existing entry and game.
- **Done when:** the import produces `v1`, the diff report runs, and the validation tests pass in CI.

### Phase 2 — Public demo (local-only)
- Angular PWA with RxDB (Dexie storage) loading the released snapshot as static files.
- National Dex checklist, per-game views, entry detail with availability and all variants, counters, Deoxys rule.
- Progress on the device; JSON export and import.
- Deployed to GitHub Pages.
- **Done when:** the demo installs as a PWA, works with the network off after the first load, and progress survives a reload.

### Phase 3 — Backend, database and release loader
- .NET GraphQL API: Pokémon data queries, profile and progress queries and mutations, validation from section 7.
- Data loader that loads a snapshot version into SQL Server; the release pipeline runs it.
- Seed builder; SQL Server in compose seeded from it.
- `POST /test/reset` (development and CI only) resets the database to the seed.
- `podman compose up` starts everything; one npm script wraps it.
- **Done when:** one command starts the stack and SQL checks confirm the seed.

### Phase 4 — Startup sync, offline and conflicts
- RxDB replication: Pokémon data pull (full first time, then changes only) and progress push/pull.
- WebSocket subscription pushes changes to the user's other devices.
- Field-level merge and conflict log; flagged progress for removed entries.
- **Done when:** a new data version reaches an open app as changes only, and every conflict case resolves the same way every time.

### Phase 5 — E2E suite and CI
- Playwright + TypeScript; Gherkin features via playwright-bdd.
- Scenarios: first sync, data version update, mark and unmark, optional details, "game obtained in" limits by generation, Deoxys rule, counters, per-game views, offline edits then sync, two-device conflicts, multi-tab, storage limits, browser suspension, export/import.
- Each test starts from `/test/reset`; no shared data between specs.
- Locator rules documented; no forced clicks or DOM scripting.
- Small Cypress smoke suite for the core path.
- GitHub Actions: on PR and nightly; Allure report on GitHub Pages; pass rate and retries tracked across runs (flakiness report).
- **Done when:** nightly runs publish a report with a pass-rate trend.

### Phase 6 — API, contract and performance
- Bruno collection: CRUD for progress, validation errors (IVs out of range, missing game when obtained, game earlier than the entry's introduction generation, unknown entry, wrong Deoxys form), auth errors, GraphQL errors behind HTTP 200.
- Rate-limit tests: bursts return 429 after the limit and recover after the window.
- Contract test (nightly, CI only): calls the live PokeAPI for a fixed set of resources and checks that the fields the import uses still exist with the same types. A failure opens a GitHub issue, which is the signal to run a new import on a branch.
- k6: load, stress, spike and soak against the CI stack (examples: many first syncs at once, many users syncing after being offline); thresholds in code; weekly in CI with summaries saved.
- **Done when:** all three run in CI and fail the job when thresholds or contracts break.

### Phase 7 — AI test generation
- Markdown specs in `/tests/specs`, one scenario per spec, with exact `data-testid`s and data.
- Opencode with a local model via Ollama and the Playwright MCP generates one test file per scenario.
- Generated tests must pass lint, format and a spec gate before they are kept.
- Track per scenario: generation time, first-run pass rate, tool errors (CSV in `/ai/metrics`).
- **Done when:** a new spec turns into a passing test with one command, documented in the README.

### Phase 8 — Sharded E2E on Kind
- Kind cluster on Podman, created in GitHub Actions.
- Each shard runs in its own pod (db, api, app, test runner) in a disposable namespace.
- Readiness probes gate test start; namespaces deleted after the run.
- Playwright `--shard` with merged reports; shards balanced by measured duration.
- **Done when:** the sharded run is faster than the single run and the comparison is in the README.

### Phase 9 — Auth, observability and production-like environment
- Keycloak realm with seeded test users (one role: user); GitHub login as an option.
- Permission tests (in CI): a user can't read or edit another user's progress, by API and through the app.
- OpenTelemetry traces and logs from the API; Grafana dashboards for sync errors, conflict volume and data version per client.
- Production-like Podman stack with `compose.prod.yaml`, loaded with the released snapshot, no test endpoints, no PokeAPI access.
- Read-only SQL checks in `/ops/queries`: loaded data version and counts, progress records per user, orphaned progress, conflict log volume.
- A documented example: a sync problem found in production logs, reproduced as a failing test in development, fixed and released.
- **Done when:** permissions are covered in CI, and the production-like environment and its checks are documented in the README.

## 10. Test strategy (summary for TEST-STRATEGY.md)

- All tests run in development and CI. Production is monitored, not tested.
- Pyramid: unit tests in app and API, snapshot validation tests, Bruno for API, a nightly PokeAPI contract test, Playwright for E2E, a few Cypress smoke tests.
- Risk-based priority: sync and conflicts first, then data isolation between users, then entry building (forms, gender and shiny variants, Deoxys), then data version updates, then offline behaviour.
- Scenario design: list the business variables (form, gender, shiny, game, online/offline, device count, data version, signed in or not), build the full matrix, then reduce with pairwise selection (PICT). Keep both in `/docs`.
- Test data: seed builder output, reset before every test, verified with SQL checks.
- Every claim in the README backed by a CI artifact or a report link.

## 11. Conventions

- TypeScript strict mode; ESLint and Prettier in app and tests.
- `data-testid` on every interactive element.
- Conventional commits; one branch per task; PRs must pass CI. PokeAPI imports always on their own `data/` branch.
- An ADR for each significant choice: manual PokeAPI import with versioned snapshots, dex entry model and IDs, Deoxys rule, conflict rule, environments and production monitoring, Podman-only stack, sharding strategy, Keycloak.

## 12. First task

Start Phase 1:
1. Create the repo layout from section 8 with placeholder READMEs. Copy section 3 into `docs/BUSINESS-RULES.md` and section 4 into `docs/DATA-LIFECYCLE.md`.
2. Build the import command in `tools/import`: fetch from PokeAPI with polite request pacing and local caching, build entries and availability, write `/data/snapshots/v1` with its manifest.
3. Add the diff report.
4. Add the snapshot validation tests.
5. Add a GitHub Actions workflow that runs the validation tests on every PR.
6. Run the first import on a `data/pokeapi-v1` branch and open a pull request with the snapshot and the diff report.

Explain each step briefly so the owner can describe it in interviews.
