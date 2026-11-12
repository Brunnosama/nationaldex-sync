# 0002. Dex entry model and IDs

- Status: accepted
- Date: 2026-11-12

## Context

The checklist is made of dex entries: one per species × form × gender variant × shininess (`docs/BUSINESS-RULES.md`). User progress is stored per entry, so entry IDs must not change between data versions. PokeAPI exposes species, Pokémon (varieties) and Pokémon forms, plus encounters per Pokémon. The business rules leave a few gaps that the importer has to fill consistently.

## Decision

### Forms

- Every Pokémon form of every variety of the species is a candidate; forms with `is_battle_only: true` (Megas, Gigantamax, in-battle forms) are left out.
- Order within a species: default variety first, then by Pokémon ID; within a variety, default form first, then `form_order`, then form ID.

### Gender split

The rules say to split species flagged `has_gender_differences`. Applied literally to every form, that creates entries that don't exist (Pikachu in a cap is male-only; Alolan Raichu has no gender difference) and doubles species whose genders PokeAPI already models as separate forms (Meowstic, Indeedee, Basculegion, Oinkologne). A form is therefore split into male and female entries when **all** of these hold:

1. the species has `has_gender_differences: true`;
2. the form has a female HOME sprite (`sprites.other.home.front_female`), so HOME shows the female differently;
3. the form's name has no `male`/`female` part (PokeAPI doesn't already model the gender as its own form).

Every other form gets one entry with `gender: null`.

### Shiny

Every entry exists as normal and shiny.

### Entry ID

`{speciesId}-{formId}-{gender}-{shiny}`, with PokeAPI species and Pokémon-form IDs, gender `m`, `f` or `x` (not split), and shiny `n` or `s`. Example: `25-25-f-s` is a female shiny Pikachu. No sequence numbers or names are used, so the same entry gets the same ID on every import.

### Introduction generation

The generation of the form's version group, but never earlier than the species' generation (PokeAPI gives Unown-A the Gen 1 version group "red-green-japan"; Unown is Gen 2). "Game obtained in" is restricted to games of this generation or later.

### Region box

The main region of the species' generation (Hisuian species introduced in Gen 8 go to the Galar box).

### Availability

- PokeAPI lists encounters per Pokémon, not per form. Every permanent form of that Pokémon gets the encounter, except in games older than the form's introduction generation (no Unown-? in Gold).
- Encounter slots with the same game, location area, method and conditions are merged: level range from the lowest minimum to the highest maximum, chance added up.
- A form is a version exclusive in a game when another game of the same version group exists and lists no encounter for that form.
- Location areas, methods and conditions are stored as PokeAPI slugs; display names are left to the app.
- Evolutions are stored separately, per species, with the trigger and every condition PokeAPI sets.

## Consequences

- The gender rule depends on HOME sprite data. If PokeAPI adds a female HOME sprite later, the next import adds the female entries and the diff report shows them as new; the existing ungendered entry (`…-x-…`) would then be removed and its progress kept and flagged. The review notes in `DIFF.md` list forms without a HOME sprite so they can be checked.
- Forms PokeAPI marks permanent but HOME doesn't store (Cosplay Pikachu, totems) are included until the business rules say otherwise.
- PokeAPI has no encounters for starters, gifts or evolved-only species in many games. Those entries are in the National Dex checklist but in no game view; that is the "PokeAPI is the only source" rule.
