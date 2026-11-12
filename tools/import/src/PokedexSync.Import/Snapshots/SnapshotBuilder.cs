using System.Text.Json;
using PokedexSync.Import.PokeApi;

namespace PokedexSync.Import.Snapshots;

/// <summary>
/// Turns raw PokeAPI data into a snapshot by applying the business rules in
/// docs/BUSINESS-RULES.md §3.1–3.2. Pure: no I/O, same input → same output.
/// </summary>
public static class SnapshotBuilder
{
    private const string English = "en";

    public static Snapshot Build(RawData raw)
    {
        var boxes = BuildBoxes(raw);
        var boxByGeneration = boxes.ToDictionary(b => b.Generation);
        var games = BuildGames(raw);
        var species = raw.Species.OrderBy(s => s.Id).ToList();

        var speciesRecords = species
            .Select(s => new SpeciesRecord(
                s.Id,
                s.Name,
                EnglishName(s.Names) ?? s.Name,
                s.Generation.Id,
                BoxFor(boxByGeneration, s).Id,
                s.HasGenderDifferences,
                s.EvolutionChain?.Id))
            .ToList();

        var entries = BuildEntries(raw, species, boxByGeneration);

        return new Snapshot
        {
            Games = games,
            Boxes = boxes,
            Species = speciesRecords,
            Entries = entries,
            Availability = BuildAvailability(raw, games, entries),
            Evolutions = BuildEvolutions(raw),
        };
    }

    /// <summary>
    /// Entry IDs are built only from PokeAPI IDs plus gender and shininess, so the same entry gets
    /// the same ID on every import: "{species}-{form}-{gender}-{shiny}", where gender is m, f, or x
    /// (no gender split) and shiny is n (normal) or s (shiny). Example: 25-25-f-s.
    /// </summary>
    public static string EntryId(int speciesId, int formId, Gender? gender, bool shiny) =>
        $"{speciesId}-{formId}-{gender switch { Gender.Male => "m", Gender.Female => "f", _ => "x" }}-{(shiny ? "s" : "n")}";

    /// <summary>
    /// A form gets male and female entries when the species has visible gender differences, HOME
    /// has a separate female sprite for this form, and PokeAPI does not already model the gender as
    /// its own form (Meowstic-Male / Meowstic-Female). See docs/adr/0002.
    /// </summary>
    public static bool SplitsByGender(Species species, PokemonForm form) =>
        species.HasGenderDifferences
        && form.Sprites.Other?.Home?.FrontFemale is not null
        && !IsGenderSpecificForm(form.Name);

    public static bool IsGenderSpecificForm(string formName) =>
        formName.Split('-').Any(part => part is "male" or "female");

    private static List<RegionBox> BuildBoxes(RawData raw) =>
        raw.Generations.Values
            .Where(g => g.MainRegion is not null)
            .OrderBy(g => g.Id)
            .Select(g =>
            {
                var region = raw.Regions[g.MainRegion!.Name];
                return new RegionBox(region.Name, EnglishName(region.Names) ?? region.Name, g.Id);
            })
            .ToList();

    private static RegionBox BoxFor(Dictionary<int, RegionBox> boxByGeneration, Species s) =>
        boxByGeneration.TryGetValue(s.Generation.Id, out var box)
            ? box
            : throw new InvalidOperationException($"Generation '{s.Generation.Name}' of species '{s.Name}' has no main region.");

    private static List<Game> BuildGames(RawData raw) =>
        raw.Versions
            .Select(v => (Version: v, Group: raw.VersionGroups[v.VersionGroup.Name]))
            .OrderBy(x => x.Group.Order)
            .ThenBy(x => x.Version.Id)
            .Select((x, i) => new Game(
                x.Version.Name,
                x.Version.Id,
                EnglishName(x.Version.Names) ?? x.Version.Name,
                x.Group.Name,
                x.Group.Generation.Id,
                i + 1))
            .ToList();

    private static List<DexEntry> BuildEntries(RawData raw, List<Species> species, Dictionary<int, RegionBox> boxByGeneration)
    {
        var entries = new List<DexEntry>();
        foreach (var s in species)
        {
            var speciesName = EnglishName(s.Names) ?? s.Name;
            var box = BoxFor(boxByGeneration, s).Id;

            var forms = s.Varieties
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.Pokemon.Id)
                .Select(v => raw.Pokemon[v.Pokemon.Id])
                .SelectMany(p => p.Forms
                    .Select(f => raw.Forms[f.Id])
                    .OrderByDescending(f => f.IsDefault)
                    .ThenBy(f => f.FormOrder)
                    .ThenBy(f => f.Id))
                .Where(f => !f.IsBattleOnly);

            foreach (var form in forms)
            {
                var introduction = IntroductionGeneration(raw, s, form);
                var genders = SplitsByGender(s, form) ? new Gender?[] { Gender.Male, Gender.Female } : [null];
                foreach (var gender in genders)
                {
                    foreach (var shiny in new[] { false, true })
                    {
                        entries.Add(new DexEntry
                        {
                            Id = EntryId(s.Id, form.Id, gender, shiny),
                            SpeciesId = s.Id,
                            FormId = form.Id,
                            PokemonId = form.Pokemon.Id,
                            Name = form.Name,
                            DisplayName = EnglishName(form.Names) ?? speciesName,
                            FormDisplayName = EnglishName(form.FormNames),
                            Gender = gender,
                            Shiny = shiny,
                            IntroductionGeneration = introduction,
                            RegionBox = box,
                            IsDefaultForm = form.IsDefault && raw.Pokemon[form.Pokemon.Id].IsDefault,
                            IsBattleOnly = form.IsBattleOnly,
                            IsMega = form.IsMega,
                            HomeSprite = HomeSprite(form, gender, shiny),
                            SortIndex = entries.Count,
                        });
                    }
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// The generation a form was introduced in: the generation of the form's version group, never
    /// earlier than the species' own generation (Unown-? is Gen 3 although Unown is Gen 2).
    /// </summary>
    private static int IntroductionGeneration(RawData raw, Species species, PokemonForm form) =>
        Math.Max(species.Generation.Id, raw.VersionGroups[form.VersionGroup.Name].Generation.Id);

    private static string? HomeSprite(PokemonForm form, Gender? gender, bool shiny)
    {
        var home = form.Sprites.Other?.Home;
        if (home is null)
            return null;
        return (gender == Gender.Female, shiny) switch
        {
            (true, false) => home.FrontFemale,
            (true, true) => home.FrontShinyFemale,
            (false, false) => home.FrontDefault,
            (false, true) => home.FrontShiny,
        };
    }

    private static List<AvailabilityRow> BuildAvailability(RawData raw, List<Game> games, List<DexEntry> entries)
    {
        var gameById = games.ToDictionary(g => g.Id);

        // Encounters are listed per Pokémon; every permanent form of that Pokémon shares them,
        // except in games older than the form itself (no Unown-? in Gold).
        var formsByPokemon = entries
            .GroupBy(e => e.PokemonId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.FormId, e.IntroductionGeneration)).Distinct().OrderBy(f => f.FormId).ToList());

        var merged = new Dictionary<(int FormId, string Game, string Area, string Method, string Conditions), (int Min, int Max, int Chance, string[] Conditions)>();
        foreach (var (pokemonId, locations) in raw.Encounters)
        {
            if (!formsByPokemon.TryGetValue(pokemonId, out var forms))
                continue;

            foreach (var location in locations)
            foreach (var versionDetail in location.VersionDetails)
            {
                if (!gameById.TryGetValue(versionDetail.Version.Name, out var game))
                    throw new InvalidOperationException($"Encounter for Pokémon {pokemonId} references unknown game '{versionDetail.Version.Name}'.");

                foreach (var encounter in versionDetail.EncounterDetails)
                {
                    var conditions = encounter.ConditionValues.Select(c => c.Name).Order(StringComparer.Ordinal).ToArray();
                    foreach (var (formId, introduction) in forms)
                    {
                        if (introduction > game.Generation)
                            continue;
                        var key = (formId, game.Id, location.LocationArea.Name, encounter.Method.Name, string.Join(',', conditions));
                        merged[key] = merged.TryGetValue(key, out var acc)
                            ? (Math.Min(acc.Min, encounter.MinLevel), Math.Max(acc.Max, encounter.MaxLevel), acc.Chance + encounter.Chance, conditions)
                            : (encounter.MinLevel, encounter.MaxLevel, encounter.Chance, conditions);
                    }
                }
            }
        }

        var gamesByForm = merged.Keys
            .GroupBy(k => k.FormId)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Game).ToHashSet());
        var gamesByGroup = games
            .GroupBy(g => g.VersionGroup)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        bool IsExclusive(int formId, string gameId)
        {
            var siblings = gamesByGroup[gameById[gameId].VersionGroup].Where(id => id != gameId).ToList();
            return siblings.Count > 0 && !siblings.Any(gamesByForm[formId].Contains);
        }

        return merged
            .Select(kv => new AvailabilityRow
            {
                FormId = kv.Key.FormId,
                Game = kv.Key.Game,
                LocationArea = kv.Key.Area,
                Method = kv.Key.Method,
                Conditions = kv.Value.Conditions,
                MinLevel = kv.Value.Min,
                MaxLevel = kv.Value.Max,
                Chance = kv.Value.Chance,
                Exclusive = IsExclusive(kv.Key.FormId, kv.Key.Game),
            })
            .OrderBy(r => r.FormId)
            .ThenBy(r => gameById[r.Game].Order)
            .ThenBy(r => r.LocationArea, StringComparer.Ordinal)
            .ThenBy(r => r.Method, StringComparer.Ordinal)
            .ThenBy(r => string.Join(',', r.Conditions), StringComparer.Ordinal)
            .ToList();
    }

    private static List<EvolutionRecord> BuildEvolutions(RawData raw)
    {
        var records = new List<EvolutionRecord>();

        void Walk(ChainLink link)
        {
            foreach (var next in link.EvolvesTo)
            {
                // A link without details is still an evolution; PokeAPI just doesn't say how.
                EvolutionDetail?[] details = next.EvolutionDetails.Count > 0 ? [.. next.EvolutionDetails] : [null];
                foreach (var detail in details)
                {
                    records.Add(new EvolutionRecord
                    {
                        FromSpeciesId = link.Species.Id,
                        ToSpeciesId = next.Species.Id,
                        Trigger = detail?.Trigger.Name,
                        Conditions = detail is null ? [] : EvolutionConditions(detail),
                    });
                }
                Walk(next);
            }
        }

        foreach (var chain in raw.EvolutionChains)
            Walk(chain.Chain);

        return records
            .OrderBy(r => r.FromSpeciesId)
            .ThenBy(r => r.ToSpeciesId)
            .ThenBy(r => r.Trigger, StringComparer.Ordinal)
            .ThenBy(r => JsonSerializer.Serialize(r.Conditions), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Keeps the conditions that are set; named resources are reduced to their slug.</summary>
    private static SortedDictionary<string, JsonElement> EvolutionConditions(EvolutionDetail detail)
    {
        var conditions = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in detail.Conditions)
        {
            var kept = value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined => (JsonElement?)null,
                JsonValueKind.String when value.GetString() is "" => null,
                JsonValueKind.Array when value.GetArrayLength() == 0 => null,
                JsonValueKind.Object when value.TryGetProperty("name", out var name) => name,
                _ => value,
            };
            if (kept is { } k)
                conditions[key] = k.Clone();
        }
        return conditions;
    }

    private static string? EnglishName(IEnumerable<LocalizedName> names) =>
        names.FirstOrDefault(n => n.Language.Name == English)?.Name;
}
