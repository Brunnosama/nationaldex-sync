# Snapshot validation tests

Vitest tests that check every committed snapshot in `data/snapshots` against the business rules: 28 Unown forms, 4 Deoxys forms, gender pairs only where flagged, a shiny version for every entry, only permanent forms, an introduction generation on every entry, unique and well-formed entry IDs, and every availability row pointing to an existing entry and game. They also check the manifest's counts and file hashes.

The tests read the JSON files directly and don't share code with the importer, so they act as an independent check of its output.

```sh
cd tests
npm ci
npm run test:data                          # every snapshot
SNAPSHOT_VERSION=v1 npm run test:data      # one snapshot
npx vitest run --project data -t "Unown"   # one test by name
```
