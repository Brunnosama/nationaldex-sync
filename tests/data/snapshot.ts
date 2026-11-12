import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// Types mirror the snapshot contract written by tools/import (docs/adr/0002-dex-entry-model.md).
// They are declared here on purpose instead of shared with the importer: the tests read the
// files the same way any consumer would.

export interface Game {
  id: string;
  pokeApiId: number;
  displayName: string;
  versionGroup: string;
  generation: number;
  order: number;
}

export interface RegionBox {
  id: string;
  displayName: string;
  generation: number;
}

export interface Species {
  id: number;
  name: string;
  displayName: string;
  generation: number;
  regionBox: string;
  hasGenderDifferences: boolean;
  evolutionChainId: number | null;
}

export interface DexEntry {
  id: string;
  speciesId: number;
  formId: number;
  pokemonId: number;
  name: string;
  displayName: string;
  formDisplayName: string | null;
  gender: 'male' | 'female' | null;
  shiny: boolean;
  introductionGeneration: number;
  regionBox: string;
  isDefaultForm: boolean;
  isBattleOnly: boolean;
  isMega: boolean;
  homeSprite: string | null;
  sortIndex: number;
}

export interface AvailabilityRow {
  formId: number;
  game: string;
  locationArea: string;
  method: string;
  conditions: string[];
  minLevel: number;
  maxLevel: number;
  chance: number;
  exclusive: boolean;
}

export interface Evolution {
  fromSpeciesId: number;
  toSpeciesId: number;
  trigger: string | null;
  conditions: Record<string, unknown>;
}

export interface Manifest {
  version: string;
  importedAt: string;
  source: string;
  counts: {
    games: number;
    boxes: number;
    species: number;
    forms: number;
    entries: number;
    availabilityRows: number;
    evolutions: number;
  };
  entriesPerGame: Record<string, number>;
  files: Record<string, string>;
}

export interface Snapshot {
  version: string;
  directory: string;
  manifest: Manifest;
  games: Game[];
  boxes: RegionBox[];
  species: Species[];
  entries: DexEntry[];
  availability: AvailabilityRow[];
  evolutions: Evolution[];
}

export const snapshotsRoot = fileURLToPath(new URL('../../data/snapshots/', import.meta.url));

/** Snapshot versions to validate: all of them, or only SNAPSHOT_VERSION (e.g. "v2") when set. */
export function snapshotVersions(): string[] {
  if (!existsSync(snapshotsRoot)) return [];
  const versions = readdirSync(snapshotsRoot)
    .filter((name) => /^v\d+$/.test(name))
    .filter((name) => existsSync(path.join(snapshotsRoot, name, 'manifest.json')))
    .sort((a, b) => Number(a.slice(1)) - Number(b.slice(1)));
  const only = process.env['SNAPSHOT_VERSION'];
  return only ? versions.filter((v) => v === only) : versions;
}

export function loadSnapshot(version: string): Snapshot {
  const directory = path.join(snapshotsRoot, version);
  const read = (file: string): unknown =>
    JSON.parse(readFileSync(path.join(directory, file), 'utf8'));
  return {
    version,
    directory,
    manifest: read('manifest.json') as Manifest,
    games: read('games.json') as Game[],
    boxes: read('boxes.json') as RegionBox[],
    species: read('species.json') as Species[],
    entries: read('entries.json') as DexEntry[],
    availability: read('availability.json') as AvailabilityRow[],
    evolutions: read('evolutions.json') as Evolution[],
  };
}

export function sha256(file: string): string {
  return createHash('sha256').update(readFileSync(file)).digest('hex');
}

/** Keeps assertion messages short: the first few offenders plus a total. */
export function sample<T>(items: T[], describe: (item: T) => string = String, max = 10): string {
  const shown = items.slice(0, max).map(describe).join(', ');
  return items.length > max ? `${shown} … (${String(items.length)} in total)` : shown;
}
