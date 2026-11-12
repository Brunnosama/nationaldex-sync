using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokedexSync.Import.PokeApi;

// Only the fields the import reads are modelled. Every one is `required`, so if PokeAPI
// renames or drops a field the deserializer throws and the import stops (see PokeApiClient).
// Nullable members may hold null, but the key itself must still be present.

public sealed record NamedRef
{
    public required string Name { get; init; }
    public required string Url { get; init; }

    /// <summary>Numeric ID taken from the resource URL (".../pokemon/25/" → 25).</summary>
    public int Id => ParseId(Url);

    public static int ParseId(string url)
    {
        var trimmed = url.TrimEnd('/');
        return int.Parse(trimmed[(trimmed.LastIndexOf('/') + 1)..]);
    }
}

public sealed record ApiResourceRef
{
    public required string Url { get; init; }
    public int Id => NamedRef.ParseId(Url);
}

public sealed record LocalizedName
{
    public required string Name { get; init; }
    public required NamedRef Language { get; init; }
}

public sealed record ResourceList
{
    public required int Count { get; init; }
    public required List<NamedRef> Results { get; init; }
}

public sealed record Species
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required NamedRef Generation { get; init; }
    public required bool HasGenderDifferences { get; init; }
    public required List<LocalizedName> Names { get; init; }
    public required List<SpeciesVariety> Varieties { get; init; }
    public required ApiResourceRef? EvolutionChain { get; init; }
}

public sealed record SpeciesVariety
{
    public required bool IsDefault { get; init; }
    public required NamedRef Pokemon { get; init; }
}

public sealed record Pokemon
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required bool IsDefault { get; init; }
    public required NamedRef Species { get; init; }
    public required List<NamedRef> Forms { get; init; }
}

public sealed record PokemonForm
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string FormName { get; init; }
    public required int FormOrder { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsBattleOnly { get; init; }
    public required bool IsMega { get; init; }
    public required NamedRef Pokemon { get; init; }
    public required NamedRef VersionGroup { get; init; }
    public required List<LocalizedName> Names { get; init; }
    public required List<LocalizedName> FormNames { get; init; }
    public required FormSprites Sprites { get; init; }
}

public sealed record FormSprites
{
    public required OtherSprites? Other { get; init; }
}

public sealed record OtherSprites
{
    public required HomeSprites? Home { get; init; }
}

public sealed record HomeSprites
{
    public required string? FrontDefault { get; init; }
    public required string? FrontShiny { get; init; }
    public required string? FrontFemale { get; init; }
    public required string? FrontShinyFemale { get; init; }
}

public sealed record GameVersion
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required List<LocalizedName> Names { get; init; }
    public required NamedRef VersionGroup { get; init; }
}

public sealed record VersionGroup
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int Order { get; init; }
    public required NamedRef Generation { get; init; }
    public required List<NamedRef> Versions { get; init; }
}

public sealed record Generation
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required NamedRef? MainRegion { get; init; }
}

public sealed record Region
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required List<LocalizedName> Names { get; init; }
}

/// <summary>One element of the array returned by /pokemon/{id}/encounters.</summary>
public sealed record LocationAreaEncounter
{
    public required NamedRef LocationArea { get; init; }
    public required List<VersionEncounterDetail> VersionDetails { get; init; }
}

public sealed record VersionEncounterDetail
{
    public required NamedRef Version { get; init; }
    public required List<Encounter> EncounterDetails { get; init; }
}

public sealed record Encounter
{
    public required int Chance { get; init; }
    public required List<NamedRef> ConditionValues { get; init; }
    public required NamedRef Method { get; init; }
    public required int MinLevel { get; init; }
    public required int MaxLevel { get; init; }
}

public sealed record EvolutionChain
{
    public required int Id { get; init; }
    public required ChainLink Chain { get; init; }
}

public sealed record ChainLink
{
    public required NamedRef Species { get; init; }
    public required List<EvolutionDetail> EvolutionDetails { get; init; }
    public required List<ChainLink> EvolvesTo { get; init; }
}

public sealed record EvolutionDetail
{
    public required NamedRef Trigger { get; init; }

    // The condition fields (min_level, item, time_of_day, …) grow as games add new
    // evolution methods, so they are kept as-is instead of being modelled one by one.
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Conditions { get; init; } = [];
}
