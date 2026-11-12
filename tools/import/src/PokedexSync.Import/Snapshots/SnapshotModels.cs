using System.Text.Json;

namespace PokedexSync.Import.Snapshots;

// The snapshot is the contract between the import and everything downstream (validation tests,
// release loader, public demo). Field changes here need a matching change in docs/adr/0002.

public sealed record Snapshot
{
    public required IReadOnlyList<Game> Games { get; init; }
    public required IReadOnlyList<RegionBox> Boxes { get; init; }
    public required IReadOnlyList<SpeciesRecord> Species { get; init; }
    public required IReadOnlyList<DexEntry> Entries { get; init; }
    public required IReadOnlyList<AvailabilityRow> Availability { get; init; }
    public required IReadOnlyList<EvolutionRecord> Evolutions { get; init; }
}

/// <summary>One PokeAPI version (FireRed, LeafGreen, …). The slug is the ID.</summary>
public sealed record Game(
    string Id,
    int PokeApiId,
    string DisplayName,
    string VersionGroup,
    int Generation,
    int Order);

/// <summary>A region box groups species by the generation that introduced them.</summary>
public sealed record RegionBox(string Id, string DisplayName, int Generation);

public sealed record SpeciesRecord(
    int Id,
    string Name,
    string DisplayName,
    int Generation,
    string RegionBox,
    bool HasGenderDifferences,
    int? EvolutionChainId);

public enum Gender
{
    Male,
    Female,
}

/// <summary>One checkable combination of species, form, gender variant and shininess.</summary>
public sealed record DexEntry
{
    public required string Id { get; init; }
    public required int SpeciesId { get; init; }
    public required int FormId { get; init; }
    public required int PokemonId { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string? FormDisplayName { get; init; }
    public required Gender? Gender { get; init; }
    public required bool Shiny { get; init; }
    public required int IntroductionGeneration { get; init; }
    public required string RegionBox { get; init; }
    public required bool IsDefaultForm { get; init; }
    public required bool IsBattleOnly { get; init; }
    public required bool IsMega { get; init; }
    public required string? HomeSprite { get; init; }
    public required int SortIndex { get; init; }
}

/// <summary>
/// Where and how a form can be found in one game, as PokeAPI reports it. Encounter slots that
/// share game, location area, method and conditions are merged: levels become a range and the
/// chances are added up.
/// </summary>
public sealed record AvailabilityRow
{
    public required int FormId { get; init; }
    public required string Game { get; init; }
    public required string LocationArea { get; init; }
    public required string Method { get; init; }
    public required IReadOnlyList<string> Conditions { get; init; }
    public required int MinLevel { get; init; }
    public required int MaxLevel { get; init; }
    public required int Chance { get; init; }

    /// <summary>True when no other game in the same version group lists this form.</summary>
    public required bool Exclusive { get; init; }
}

/// <summary>One way to evolve, as listed in a PokeAPI evolution chain.</summary>
public sealed record EvolutionRecord
{
    public required int FromSpeciesId { get; init; }
    public required int ToSpeciesId { get; init; }
    public required string? Trigger { get; init; }
    public required SortedDictionary<string, JsonElement> Conditions { get; init; }
}

public sealed record Manifest
{
    public required string Version { get; init; }
    public required DateTimeOffset ImportedAt { get; init; }
    public required string Source { get; init; }
    public required ManifestCounts Counts { get; init; }
    public required SortedDictionary<string, int> EntriesPerGame { get; init; }
    public required SortedDictionary<string, string> Files { get; init; }
}

public sealed record ManifestCounts(
    int Games,
    int Boxes,
    int Species,
    int Forms,
    int Entries,
    int AvailabilityRows,
    int Evolutions);
