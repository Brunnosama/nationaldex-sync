using PokedexSync.Import.PokeApi;

namespace PokedexSync.Import.Tests;

/// <summary>Builds small in-memory PokeAPI datasets for the snapshot builder tests.</summary>
internal sealed class Fake
{
    private const string Api = PokeApiClient.BaseUrl;

    private readonly List<Species> _species = [];
    private readonly Dictionary<int, Pokemon> _pokemon = [];
    private readonly Dictionary<int, PokemonForm> _forms = [];
    private readonly Dictionary<int, IReadOnlyList<LocationAreaEncounter>> _encounters = [];
    private readonly List<EvolutionChain> _chains = [];

    public static NamedRef Ref(string resource, int id, string name) => new() { Name = name, Url = $"{Api}{resource}/{id}/" };

    public static List<LocalizedName> English(string name) => [new() { Name = name, Language = Ref("language", 9, "en") }];

    public Fake Species(int id, string name, int generation, bool genderDifferences = false, int? chain = null)
    {
        _species.Add(new Species
        {
            Id = id,
            Name = name,
            Generation = Ref("generation", generation, $"generation-{generation}"),
            HasGenderDifferences = genderDifferences,
            Names = English(Capitalize(name)),
            Varieties = [],
            EvolutionChain = chain is null ? null : new ApiResourceRef { Url = $"{Api}evolution-chain/{chain}/" },
        });
        return this;
    }

    /// <summary>Adds a Pokémon (variety) with one form per entry in <paramref name="forms"/>.</summary>
    public Fake Pokemon(int speciesId, int pokemonId, string name, bool isDefault, params FormSpec[] forms)
    {
        var species = _species.Single(s => s.Id == speciesId);
        var index = _species.IndexOf(species);
        _species[index] = species with
        {
            Varieties = [.. species.Varieties, new SpeciesVariety { IsDefault = isDefault, Pokemon = Ref("pokemon", pokemonId, name) }],
        };

        var formSpecs = forms.Length > 0 ? forms : [new FormSpec(pokemonId, name)];
        _pokemon[pokemonId] = new Pokemon
        {
            Id = pokemonId,
            Name = name,
            IsDefault = isDefault,
            Species = Ref("pokemon-species", speciesId, species.Name),
            Forms = formSpecs.Select(f => Ref("pokemon-form", f.Id, f.Name)).ToList(),
        };

        foreach (var (spec, order) in formSpecs.Select((f, i) => (f, i + 1)))
        {
            _forms[spec.Id] = new PokemonForm
            {
                Id = spec.Id,
                Name = spec.Name,
                FormName = spec.Name.Contains('-') ? spec.Name[(spec.Name.IndexOf('-') + 1)..] : "",
                FormOrder = order,
                IsDefault = order == 1,
                IsBattleOnly = spec.BattleOnly,
                IsMega = spec.BattleOnly && spec.Name.Contains("mega"),
                Pokemon = Ref("pokemon", pokemonId, name),
                VersionGroup = Ref("version-group", 100 + (spec.Generation ?? species.Generation.Id), $"vg-{spec.Generation ?? species.Generation.Id}"),
                Names = spec.DisplayName is null ? [] : English(spec.DisplayName),
                FormNames = [],
                Sprites = new FormSprites
                {
                    Other = new OtherSprites
                    {
                        Home = new HomeSprites
                        {
                            FrontDefault = $"home/{spec.Id}.png",
                            FrontShiny = $"home/shiny/{spec.Id}.png",
                            FrontFemale = spec.FemaleSprite ? $"home/female/{spec.Id}.png" : null,
                            FrontShinyFemale = spec.FemaleSprite ? $"home/shiny/female/{spec.Id}.png" : null,
                        },
                    },
                },
            };
        }
        return this;
    }

    public Fake Encounter(int pokemonId, string area, string game, string method, int chance, int minLevel, int maxLevel, params string[] conditions)
    {
        var existing = _encounters.TryGetValue(pokemonId, out var list) ? list.ToList() : [];
        existing.Add(new LocationAreaEncounter
        {
            LocationArea = Ref("location-area", 1, area),
            VersionDetails =
            [
                new VersionEncounterDetail
                {
                    Version = Ref("version", 1, game),
                    EncounterDetails =
                    [
                        new Encounter
                        {
                            Chance = chance,
                            ConditionValues = conditions.Select(c => Ref("encounter-condition-value", 1, c)).ToList(),
                            Method = Ref("encounter-method", 1, method),
                            MinLevel = minLevel,
                            MaxLevel = maxLevel,
                        },
                    ],
                },
            ],
        });
        _encounters[pokemonId] = existing;
        return this;
    }

    public Fake Chain(EvolutionChain chain)
    {
        _chains.Add(chain);
        return this;
    }

    /// <summary>
    /// Games: generation N has version group "vg-N". Gen 1 has red and blue, gen 3 has firered and
    /// leafgreen, every other generation has a single game "game-N".
    /// </summary>
    public RawData Build()
    {
        var versionGroups = Enumerable.Range(1, 9).Select(gen => new VersionGroup
        {
            Id = 100 + gen,
            Name = $"vg-{gen}",
            Order = gen,
            Generation = Ref("generation", gen, $"generation-{gen}"),
            Versions = GamesOf(gen).Select((g, i) => Ref("version", gen * 10 + i, g)).ToList(),
        }).ToList();

        return new RawData
        {
            Species = _species,
            Pokemon = _pokemon,
            Forms = _forms,
            Encounters = _encounters,
            EvolutionChains = _chains,
            Versions = versionGroups.SelectMany(vg => vg.Versions.Select(v => new GameVersion
            {
                Id = v.Id,
                Name = v.Name,
                Names = English(Capitalize(v.Name)),
                VersionGroup = Ref("version-group", vg.Id, vg.Name),
            })).ToList(),
            VersionGroups = versionGroups.ToDictionary(v => v.Name),
            Generations = Enumerable.Range(1, 9).Select(gen => new Generation
            {
                Id = gen,
                Name = $"generation-{gen}",
                MainRegion = Ref("region", gen, $"region-{gen}"),
            }).ToDictionary(g => g.Name),
            Regions = Enumerable.Range(1, 9).Select(gen => new Region
            {
                Id = gen,
                Name = $"region-{gen}",
                Names = English($"Region {gen}"),
            }).ToDictionary(r => r.Name),
        };
    }

    private static string[] GamesOf(int generation) => generation switch
    {
        1 => ["red", "blue"],
        3 => ["firered", "leafgreen"],
        _ => [$"game-{generation}"],
    };

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}

internal sealed record FormSpec(
    int Id,
    string Name,
    bool BattleOnly = false,
    bool FemaleSprite = false,
    int? Generation = null,
    string? DisplayName = null);
