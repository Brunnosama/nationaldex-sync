# Pokémon data snapshots

Each `vN/` folder is one released data version, written by [`tools/import`](../../tools/import) on a `data/pokeapi-vN` branch and merged after review and CI ([`docs/DATA-LIFECYCLE.md`](../../docs/DATA-LIFECYCLE.md)). Snapshots are never edited by hand; corrections are made on the import branch as code changes and re-imported. CI runs the validation tests in [`tests/data`](../../tests/data) on every snapshot.

| File | Content |
| --- | --- |
| `manifest.json` | Version, import date, source, counts, entries per game, SHA-256 of each data file |
| `DIFF.md` | Diff report against the previous version |
| `entries.json` | Dex entries: species × form × gender variant × shininess, sorted for display |
| `species.json` | Species: name, generation, region box, gender-difference flag, evolution chain |
| `availability.json` | Where each form is found in each game: location area, method, conditions, level range, chance, version-exclusive flag |
| `evolutions.json` | Evolution steps with trigger and conditions |
| `games.json` | Every PokeAPI version, with version group and generation |
| `boxes.json` | Region boxes (one per generation's main region) |

Entry IDs and field meanings: [ADR 0002](../../docs/adr/0002-dex-entry-model.md).
