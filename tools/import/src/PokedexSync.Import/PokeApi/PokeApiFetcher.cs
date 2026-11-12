namespace PokedexSync.Import.PokeApi;

/// <summary>Everything the snapshot builder needs, exactly as PokeAPI returned it.</summary>
public sealed record RawData
{
    public required IReadOnlyList<Species> Species { get; init; }
    public required IReadOnlyDictionary<int, Pokemon> Pokemon { get; init; }
    public required IReadOnlyDictionary<int, PokemonForm> Forms { get; init; }
    public required IReadOnlyDictionary<int, IReadOnlyList<LocationAreaEncounter>> Encounters { get; init; }
    public required IReadOnlyList<EvolutionChain> EvolutionChains { get; init; }
    public required IReadOnlyList<GameVersion> Versions { get; init; }
    public required IReadOnlyDictionary<string, VersionGroup> VersionGroups { get; init; }
    public required IReadOnlyDictionary<string, Generation> Generations { get; init; }
    public required IReadOnlyDictionary<string, Region> Regions { get; init; }
}

public sealed class PokeApiFetcher(PokeApiClient client, TextWriter log)
{
    /// <param name="speciesLimit">Only for quick local runs: fetch the first N species.</param>
    public async Task<RawData> FetchAsync(int? speciesLimit = null, CancellationToken ct = default)
    {
        log.WriteLine("Fetching games…");
        var versions = await FetchAllAsync<GameVersion>(await client.ListAsync("version", ct), ct);
        var versionGroups = await FetchAllAsync<VersionGroup>(await client.ListAsync("version-group", ct), ct);
        var generations = await FetchAllAsync<Generation>(await client.ListAsync("generation", ct), ct);
        var regions = await FetchAllAsync<Region>(await client.ListAsync("region", ct), ct);

        log.WriteLine("Fetching species…");
        var speciesRefs = (await client.ListAsync("pokemon-species", ct)).Results
            .OrderBy(r => r.Id)
            .Take(speciesLimit ?? int.MaxValue)
            .ToList();
        var species = await FetchAllAsync<Species>(speciesRefs, ct);

        log.WriteLine("Fetching Pokémon and forms…");
        var pokemon = new Dictionary<int, Pokemon>();
        var forms = new Dictionary<int, PokemonForm>();
        foreach (var variety in species.SelectMany(s => s.Varieties))
        {
            var p = await client.GetAsync<Pokemon>(variety.Pokemon.Url, ct);
            pokemon[p.Id] = p;
            foreach (var formRef in p.Forms)
            {
                var form = await client.GetAsync<PokemonForm>(formRef.Url, ct);
                forms[form.Id] = form;
            }
        }

        log.WriteLine("Fetching encounters…");
        var encounters = new Dictionary<int, IReadOnlyList<LocationAreaEncounter>>();
        // Encounters are only needed for Pokémon that have at least one permanent form.
        foreach (var pokemonId in forms.Values.Where(f => !f.IsBattleOnly).Select(f => f.Pokemon.Id).Distinct().Order())
            encounters[pokemonId] = await client.GetAsync<List<LocationAreaEncounter>>($"pokemon/{pokemonId}/encounters", ct);

        log.WriteLine("Fetching evolution chains…");
        var chains = new List<EvolutionChain>();
        foreach (var chainId in species.Where(s => s.EvolutionChain is not null).Select(s => s.EvolutionChain!.Id).Distinct().Order())
            chains.Add(await client.GetAsync<EvolutionChain>($"evolution-chain/{chainId}", ct));

        return new RawData
        {
            Species = species,
            Pokemon = pokemon,
            Forms = forms,
            Encounters = encounters,
            EvolutionChains = chains,
            Versions = versions,
            VersionGroups = versionGroups.ToDictionary(v => v.Name),
            Generations = generations.ToDictionary(g => g.Name),
            Regions = regions.ToDictionary(r => r.Name),
        };
    }

    private Task<List<T>> FetchAllAsync<T>(ResourceList list, CancellationToken ct) =>
        FetchAllAsync<T>(list.Results.OrderBy(r => r.Id).ToList(), ct);

    private async Task<List<T>> FetchAllAsync<T>(IEnumerable<NamedRef> refs, CancellationToken ct)
    {
        var results = new List<T>();
        foreach (var r in refs)
            results.Add(await client.GetAsync<T>(r.Url, ct));
        return results;
    }
}
