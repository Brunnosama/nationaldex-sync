# Test strategy

Summary from section 10 of `HANDOFF.md`. To be expanded as each phase adds test layers.

- All tests run in development and CI. Production is monitored, not tested.
- Pyramid: unit tests in app and API, snapshot validation tests, Bruno for API, a nightly PokeAPI contract test, Playwright for E2E.
- Risk-based priority: sync and conflicts first, then data isolation between users, then entry building (forms, gender and shiny variants, Deoxys), then data version updates, then offline behaviour.
- Scenario design: list the business variables (form, gender, shiny, game, online/offline, device count, data version, signed in or not), build the full matrix, then reduce with pairwise selection (PICT). Keep both in `/docs`.
- Test data: seed builder output, reset before every test, verified with SQL checks.
- Every claim in the README backed by a CI artifact or a report link.
