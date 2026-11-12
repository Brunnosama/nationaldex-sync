# Data lifecycle

Copied from section 4 of `HANDOFF.md`.

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
