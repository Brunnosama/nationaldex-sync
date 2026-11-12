import path from 'node:path';
import { beforeAll, describe, expect, it } from 'vitest';
import {
  type DexEntry,
  type Snapshot,
  loadSnapshot,
  sample,
  sha256,
  snapshotVersions,
} from './snapshot.js';

// Validates every committed Pokémon data snapshot against the rules in docs/BUSINESS-RULES.md.
// A snapshot only reaches users after these pass in CI (docs/DATA-LIFECYCLE.md).

const UNOWN = 201;
const DEOXYS = 386;

function expectNone<T>(offenders: T[], rule: string, describeItem: (item: T) => string = String) {
  expect(offenders, `${rule}. Offenders: ${sample(offenders, describeItem)}`).toHaveLength(0);
}

const formKey = (e: DexEntry) => `${String(e.speciesId)}-${String(e.formId)}`;

const versions = snapshotVersions();

describe.skipIf(versions.length === 0)('Pokémon data snapshots', () => {
  describe.each(versions)('snapshot %s', (version) => {
    let s: Snapshot;
    beforeAll(() => {
      s = loadSnapshot(version);
    });

    describe('manifest', () => {
      it('names this version', () => {
        expect(s.manifest.version).toBe(version);
      });

      it('has a hash for every data file that matches the file on disk', () => {
        const mismatched = Object.entries(s.manifest.files).filter(
          ([file, hash]) => sha256(path.join(s.directory, file)) !== hash,
        );
        expectNone(mismatched, 'Data file changed after the import wrote it', ([file]) => file);
      });

      it('has counts that match the data files', () => {
        expect(s.manifest.counts).toEqual({
          games: s.games.length,
          boxes: s.boxes.length,
          species: s.species.length,
          forms: new Set(s.entries.map((e) => e.formId)).size,
          entries: s.entries.length,
          availabilityRows: s.availability.length,
          evolutions: s.evolutions.length,
        });
      });

      it('lists an entry count for every game', () => {
        expect(Object.keys(s.manifest.entriesPerGame).sort()).toEqual(
          s.games.map((g) => g.id).sort(),
        );
      });
    });

    describe('dex entries', () => {
      it('have unique IDs', () => {
        const seen = new Set<string>();
        const duplicates = s.entries.filter((e) => seen.size === seen.add(e.id).size);
        expectNone(duplicates, 'Duplicate entry IDs', (e) => e.id);
      });

      it('have IDs built from species, form, gender and shininess', () => {
        const wrong = s.entries.filter((e) => {
          const gender = e.gender === 'male' ? 'm' : e.gender === 'female' ? 'f' : 'x';
          return (
            e.id !== `${String(e.speciesId)}-${String(e.formId)}-${gender}-${e.shiny ? 's' : 'n'}`
          );
        });
        expectNone(wrong, 'Entry ID does not match its fields', (e) => e.id);
      });

      it('include all 28 Unown forms', () => {
        const forms = new Set(s.entries.filter((e) => e.speciesId === UNOWN).map((e) => e.formId));
        expect(forms.size).toBe(28);
      });

      it('include the 4 Deoxys forms', () => {
        const forms = new Set(s.entries.filter((e) => e.speciesId === DEOXYS).map((e) => e.name));
        expect([...forms].sort()).toEqual([
          'deoxys-attack',
          'deoxys-defense',
          'deoxys-normal',
          'deoxys-speed',
        ]);
      });

      it('have a shiny version for every normal entry, and the other way round', () => {
        const ids = new Set(s.entries.map((e) => e.id));
        const unpaired = s.entries.filter(
          (e) => !ids.has(e.id.slice(0, -1) + (e.shiny ? 'n' : 's')),
        );
        expectNone(unpaired, 'Entry without its shiny/normal counterpart', (e) => e.id);
      });

      it('are split by gender only for species flagged with gender differences', () => {
        const flagged = new Set(
          s.species.filter((sp) => sp.hasGenderDifferences).map((sp) => sp.id),
        );
        const wrong = s.entries.filter((e) => e.gender !== null && !flagged.has(e.speciesId));
        expectNone(wrong, 'Gender split on a species without gender differences', (e) => e.id);
      });

      it('come in male/female pairs when a form is split by gender', () => {
        const ids = new Set(s.entries.map((e) => e.id));
        const swap = (e: DexEntry) =>
          e.id.replace(
            /-(m|f)-(n|s)$/,
            (_, g: string, sh: string) => `-${g === 'm' ? 'f' : 'm'}-${sh}`,
          );
        const unpaired = s.entries.filter((e) => e.gender !== null && !ids.has(swap(e)));
        expectNone(unpaired, 'Gendered entry without its other gender', (e) => e.id);
      });

      it('never mix split and unsplit entries for the same form', () => {
        const byForm = Map.groupBy(s.entries, formKey);
        const mixed = [...byForm].filter(
          ([, es]) => new Set(es.map((e) => e.gender === null)).size > 1,
        );
        expectNone(mixed, 'Form has both gendered and ungendered entries', ([key]) => key);
      });

      it('only include permanent forms', () => {
        const battleOnly = s.entries.filter((e) => e.isBattleOnly || e.isMega);
        expectNone(battleOnly, 'Battle-only form in the checklist', (e) => e.name);
      });

      it('each have an introduction generation, never before their species', () => {
        const generations = new Set(s.boxes.map((b) => b.generation));
        const species = new Map(s.species.map((sp) => [sp.id, sp]));
        const wrong = s.entries.filter(
          (e) =>
            !Number.isInteger(e.introductionGeneration) ||
            !generations.has(e.introductionGeneration) ||
            e.introductionGeneration < (species.get(e.speciesId)?.generation ?? Infinity),
        );
        expectNone(wrong, 'Missing or invalid introduction generation', (e) => e.id);
      });

      it('belong to an existing species and its region box', () => {
        const species = new Map(s.species.map((sp) => [sp.id, sp]));
        const boxes = new Set(s.boxes.map((b) => b.id));
        const wrong = s.entries.filter(
          (e) => species.get(e.speciesId)?.regionBox !== e.regionBox || !boxes.has(e.regionBox),
        );
        expectNone(wrong, 'Entry with unknown species or wrong region box', (e) => e.id);
      });

      it('are sorted by National Dex number, with a sort index in file order', () => {
        const outOfOrder = s.entries.filter(
          (e, i) =>
            e.sortIndex !== i || (i > 0 && e.speciesId < (s.entries[i - 1]?.speciesId ?? 0)),
        );
        expectNone(outOfOrder, 'Entry out of order', (e) => e.id);
      });
    });

    describe('games', () => {
      it('have unique IDs and a known generation', () => {
        const generations = new Set(s.boxes.map((b) => b.generation));
        expect(new Set(s.games.map((g) => g.id)).size).toBe(s.games.length);
        expectNone(
          s.games.filter((g) => !generations.has(g.generation)),
          'Game with unknown generation',
          (g) => g.id,
        );
      });
    });

    describe('availability', () => {
      it('points every row to an existing entry and game', () => {
        const forms = new Set(s.entries.map((e) => e.formId));
        const games = new Set(s.games.map((g) => g.id));
        const orphans = s.availability.filter((r) => !forms.has(r.formId) || !games.has(r.game));
        expectNone(
          orphans,
          'Availability row without entry or game',
          (r) => `${String(r.formId)}@${r.game}`,
        );
      });

      it('never lists a form in a game older than the form', () => {
        const introduced = new Map(s.entries.map((e) => [e.formId, e.introductionGeneration]));
        const generation = new Map(s.games.map((g) => [g.id, g.generation]));
        const wrong = s.availability.filter(
          (r) => (introduced.get(r.formId) ?? 0) > (generation.get(r.game) ?? Infinity),
        );
        expectNone(
          wrong,
          'Form listed before it was introduced',
          (r) => `${String(r.formId)}@${r.game}`,
        );
      });

      it('has a valid level range', () => {
        const wrong = s.availability.filter(
          (r) => r.minLevel < 1 || r.minLevel > r.maxLevel || r.maxLevel > 100,
        );
        expectNone(
          wrong,
          'Invalid level range',
          (r) => `${String(r.formId)}@${r.game}/${r.locationArea}`,
        );
      });
    });

    describe('evolutions', () => {
      it('connect existing species', () => {
        const species = new Set(s.species.map((sp) => sp.id));
        const orphans = s.evolutions.filter(
          (ev) => !species.has(ev.fromSpeciesId) || !species.has(ev.toSpeciesId),
        );
        expectNone(
          orphans,
          'Evolution to or from unknown species',
          (ev) => `${String(ev.fromSpeciesId)}→${String(ev.toSpeciesId)}`,
        );
      });
    });
  });
});
