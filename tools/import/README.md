# PokeAPI import

.NET console app that pulls Pokémon data from [PokeAPI](https://pokeapi.co), applies the rules in [`docs/BUSINESS-RULES.md`](../../docs/BUSINESS-RULES.md) and writes the next versioned snapshot to `data/snapshots/vN` with a diff report. **Development only**, run by hand, never by CI or production ([ADR 0001](../../docs/adr/0001-manual-pokeapi-import.md)).

## Commands

Run from the repository root.

```sh
# Preview: build the next snapshot and print the diff report against the latest one. Writes nothing.
dotnet run --project tools/import/src/PokedexSync.Import -- import --dry-run

# Quick local check with the first 30 species only (dry runs only).
dotnet run --project tools/import/src/PokedexSync.Import -- import --dry-run --species-limit 30

# Real import: must run on a data/* branch (e.g. data/pokeapi-v2). Writes data/snapshots/vN + DIFF.md.
git switch -c data/pokeapi-v2
dotnet run --project tools/import/src/PokedexSync.Import -- import

# Diff two existing snapshots.
dotnet run --project tools/import/src/PokedexSync.Import -- diff data/snapshots/v1 data/snapshots/v2

# Unit tests (no network).
dotnet test tools/import/Import.slnx
dotnet test tools/import/Import.slnx --filter "FullyQualifiedName~SnapshotBuilderTests.Battle_only_forms_are_left_out"
```

## How it works

1. **Fetch** (`PokeApi/PokeApiFetcher.cs`): games (versions, version groups, generations, regions), every species, its Pokémon and forms, encounters for each Pokémon with a permanent form, and evolution chains. About 6,000 requests.
2. **Pace and cache** (`PokeApi/PokeApiClient.cs`): one request at a time with at least 150 ms between them (`--delay-ms`), retries with back-off on 429/5xx, and every response body is cached in `tools/import/.cache/` (git-ignored). A second run, such as a dry run after an import, makes no requests. Delete the cache to pull fresh data.
3. **Format check**: each response is deserialized into records whose fields are all `required`. If PokeAPI renames, removes or retypes a field the import uses, it stops with exit code 2 and names the resource.
4. **Build** (`Snapshots/SnapshotBuilder.cs`): pure function from raw data to snapshot. Rules and their reasons are in [ADR 0002](../../docs/adr/0002-dex-entry-model.md).
5. **Write** (`Snapshots/SnapshotStore.cs`): JSON arrays with one record per line, so git diffs stay readable, plus `manifest.json` with counts and a SHA-256 for each file.
6. **Diff report** (`Snapshots/SnapshotDiff.cs`): entries added, removed and changed, and availability changed per form and game. Saved as `DIFF.md` next to the snapshot and pasted into the pull request.

Exit codes: `0` success, `1` error (network, wrong branch, bad arguments), `2` PokeAPI format changed.
