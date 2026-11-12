# nationaldex-sync

National Pokédex PWA to help completionists track their collection across every Pokémon game: each Pokémon and form, the games it appears in and how to obtain it there, and a private checklist that syncs between devices and works offline.

This is a QA portfolio project: the app exists to be tested, and the test suite and CI pipelines are the real deliverable. The plan and acceptance criteria per phase are in [HANDOFF.md](HANDOFF.md).

## Status

Phase 1, Pokémon data import and first snapshot:

- [`tools/import`](tools/import): manual PokeAPI import (development only) that writes versioned snapshots to [`data/snapshots`](data/snapshots) with a diff report.
- [`tests/data`](tests/data): snapshot validation tests, run in CI on every pull request.

## Docs

- [Business rules](docs/BUSINESS-RULES.md)
- [Data lifecycle](docs/DATA-LIFECYCLE.md)
- [Test strategy](docs/TEST-STRATEGY.md)
- [Architecture decisions](docs/adr)
