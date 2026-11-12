# Business rules

Copied from section 3 of `HANDOFF.md`. This file is the one to keep up to date.

The rules come from the owner's existing personal tracker.

## Dex entries (what can be checked)
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

## Availability (where and how to get it)
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

## Tracking progress (per user)
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

## Game-dependent form rule (Gen 3 Deoxys)
In Gen 3 games, Deoxys has no form of its own; the game decides it. When a user marks Deoxys as obtained in a Gen 3 game, the form entry is set automatically:

| Game | Entry |
| --- | --- |
| FireRed | Deoxys-A (Attack) |
| LeafGreen | Deoxys-D (Defense) |
| Emerald | Deoxys-S (Speed) |
| Ruby, Sapphire, Colosseum, XD | Deoxys-N (Normal) |

From Gen 4 on, the user picks the form. All other forms (Unown included) are picked by the user in every game. The Deoxys rule and the "game obtained in" rule are applied in the app and enforced by the API.

## Startup sync and offline use
- On start, the app syncs with the server through RxDB replication (GraphQL + WebSocket):
  - **Pokémon data** (read-only): the full entry list, forms, games and availability for every game. The first sync downloads everything; later syncs download only what changed in a new data version.
  - **Progress** (read/write): the user's checklist, both directions.
- After the first sync, the app works fully offline. It shows when it is offline and how many changes are waiting to sync.
