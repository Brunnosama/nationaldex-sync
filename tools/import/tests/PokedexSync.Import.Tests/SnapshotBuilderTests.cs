using PokedexSync.Import.PokeApi;
using PokedexSync.Import.Snapshots;

namespace PokedexSync.Import.Tests;

public class SnapshotBuilderTests
{
    [Theory]
    [InlineData(25, 25, Gender.Female, true, "25-25-f-s")]
    [InlineData(25, 25, Gender.Male, false, "25-25-m-n")]
    [InlineData(26, 10100, null, false, "26-10100-x-n")]
    public void Entry_id_is_built_from_pokeapi_ids_gender_and_shininess(int species, int form, Gender? gender, bool shiny, string expected)
    {
        Assert.Equal(expected, SnapshotBuilder.EntryId(species, form, gender, shiny));
    }

    [Fact]
    public void Every_form_gets_a_normal_and_a_shiny_entry()
    {
        var raw = new Fake().Species(1, "bulbasaur", 1).Pokemon(1, 1, "bulbasaur", isDefault: true).Build();

        var entries = SnapshotBuilder.Build(raw).Entries;

        Assert.Equal(["1-1-x-n", "1-1-x-s"], entries.Select(e => e.Id));
        Assert.All(entries, e => Assert.Null(e.Gender));
    }

    [Fact]
    public void Form_is_split_by_gender_when_species_is_flagged_and_home_has_a_female_sprite()
    {
        var raw = new Fake()
            .Species(3, "venusaur", 1, genderDifferences: true)
            .Pokemon(3, 3, "venusaur", isDefault: true, new FormSpec(3, "venusaur", FemaleSprite: true))
            .Build();

        var entries = SnapshotBuilder.Build(raw).Entries;

        Assert.Equal(["3-3-m-n", "3-3-m-s", "3-3-f-n", "3-3-f-s"], entries.Select(e => e.Id));
        Assert.Equal("home/shiny/female/3.png", entries.Single(e => e.Id == "3-3-f-s").HomeSprite);
    }

    [Fact]
    public void Alternate_form_without_a_female_sprite_is_not_split_even_if_species_is_flagged()
    {
        // Pikachu has gender differences; Pikachu in a cap does not.
        var raw = new Fake()
            .Species(25, "pikachu", 1, genderDifferences: true)
            .Pokemon(25, 25, "pikachu", isDefault: true, new FormSpec(25, "pikachu", FemaleSprite: true))
            .Pokemon(25, 10094, "pikachu-original-cap", isDefault: false, new FormSpec(10094, "pikachu-original-cap", Generation: 7))
            .Build();

        var capEntries = SnapshotBuilder.Build(raw).Entries.Where(e => e.FormId == 10094);

        Assert.Equal(["25-10094-x-n", "25-10094-x-s"], capEntries.Select(e => e.Id));
    }

    [Fact]
    public void Form_that_already_is_a_gender_is_not_split_again()
    {
        // PokeAPI flags Meowstic with gender differences and also models each gender as a form.
        var raw = new Fake()
            .Species(678, "meowstic", 6, genderDifferences: true)
            .Pokemon(678, 678, "meowstic-male", isDefault: true, new FormSpec(678, "meowstic-male", FemaleSprite: true))
            .Pokemon(678, 10025, "meowstic-female", isDefault: false, new FormSpec(10025, "meowstic-female"))
            .Build();

        var entries = SnapshotBuilder.Build(raw).Entries;

        Assert.Equal(["678-678-x-n", "678-678-x-s", "678-10025-x-n", "678-10025-x-s"], entries.Select(e => e.Id));
    }

    [Fact]
    public void Battle_only_forms_are_left_out()
    {
        var raw = new Fake()
            .Species(3, "venusaur", 1)
            .Pokemon(3, 3, "venusaur", isDefault: true)
            .Pokemon(3, 10033, "venusaur-mega", isDefault: false, new FormSpec(10033, "venusaur-mega", BattleOnly: true, Generation: 6))
            .Build();

        var entries = SnapshotBuilder.Build(raw).Entries;

        Assert.DoesNotContain(entries, e => e.FormId == 10033);
        Assert.All(entries, e => Assert.False(e.IsBattleOnly));
    }

    [Fact]
    public void Introduction_generation_is_the_form_generation_but_never_before_the_species()
    {
        var raw = new Fake()
            .Species(201, "unown", 2)
            .Pokemon(201, 201, "unown", isDefault: true,
                new FormSpec(201, "unown-a", Generation: 1),
                new FormSpec(10027, "unown-question", Generation: 3))
            .Build();

        var entries = SnapshotBuilder.Build(raw).Entries;

        Assert.Equal(2, entries.First(e => e.FormId == 201).IntroductionGeneration);
        Assert.Equal(3, entries.First(e => e.FormId == 10027).IntroductionGeneration);
    }

    [Fact]
    public void Entries_are_sorted_by_dex_number_then_form_gender_and_shininess()
    {
        var raw = new Fake()
            .Species(26, "raichu", 1, genderDifferences: true)
            .Species(25, "pikachu", 1)
            .Pokemon(26, 10100, "raichu-alola", isDefault: false, new FormSpec(10100, "raichu-alola", Generation: 7))
            .Pokemon(26, 26, "raichu", isDefault: true, new FormSpec(26, "raichu", FemaleSprite: true))
            .Pokemon(25, 25, "pikachu", isDefault: true)
            .Build();

        var entries = SnapshotBuilder.Build(raw).Entries;

        Assert.Equal(
            ["25-25-x-n", "25-25-x-s", "26-26-m-n", "26-26-m-s", "26-26-f-n", "26-26-f-s", "26-10100-x-n", "26-10100-x-s"],
            entries.Select(e => e.Id));
        Assert.Equal(Enumerable.Range(0, entries.Count), entries.Select(e => e.SortIndex));
    }

    [Fact]
    public void Species_is_placed_in_the_region_box_of_its_generation()
    {
        var raw = new Fake().Species(152, "chikorita", 2).Pokemon(152, 152, "chikorita", isDefault: true).Build();

        var snapshot = SnapshotBuilder.Build(raw);

        Assert.All(snapshot.Entries, e => Assert.Equal("region-2", e.RegionBox));
        Assert.Equal("region-2", snapshot.Species.Single().RegionBox);
    }

    [Fact]
    public void Encounter_slots_with_the_same_place_method_and_conditions_are_merged()
    {
        var raw = new Fake()
            .Species(16, "pidgey", 1)
            .Pokemon(16, 16, "pidgey", isDefault: true)
            .Encounter(16, "route-1-area", "red", "walk", chance: 15, minLevel: 2, maxLevel: 3)
            .Encounter(16, "route-1-area", "red", "walk", chance: 20, minLevel: 4, maxLevel: 5)
            .Encounter(16, "route-1-area", "red", "walk", chance: 5, minLevel: 3, maxLevel: 3, "time-night")
            .Build();

        var rows = SnapshotBuilder.Build(raw).Availability;

        Assert.Equal(2, rows.Count);
        var day = rows.Single(r => r.Conditions.Count == 0);
        Assert.Equal((2, 5, 35), (day.MinLevel, day.MaxLevel, day.Chance));
        Assert.Equal(["time-night"], rows.Single(r => r.Conditions.Count == 1).Conditions);
    }

    [Fact]
    public void Encounters_apply_to_every_form_of_the_pokemon_but_not_in_games_older_than_the_form()
    {
        var raw = new Fake()
            .Species(201, "unown", 2)
            .Pokemon(201, 201, "unown", isDefault: true,
                new FormSpec(201, "unown-a"),
                new FormSpec(10027, "unown-question", Generation: 3))
            .Encounter(201, "ruins-of-alph", "game-2", "walk", 100, 5, 5)
            .Encounter(201, "tanoby-chambers", "firered", "walk", 100, 25, 25)
            .Build();

        var rows = SnapshotBuilder.Build(raw).Availability;

        Assert.Equal(["game-2", "firered"], rows.Where(r => r.FormId == 201).Select(r => r.Game));
        Assert.Equal(["firered"], rows.Where(r => r.FormId == 10027).Select(r => r.Game));
    }

    [Fact]
    public void Form_found_in_only_one_game_of_its_version_group_is_flagged_exclusive()
    {
        var raw = new Fake()
            .Species(23, "ekans", 1)
            .Pokemon(23, 23, "ekans", isDefault: true)
            .Species(16, "pidgey", 1)
            .Pokemon(16, 16, "pidgey", isDefault: true)
            .Encounter(23, "route-4-area", "red", "walk", 25, 6, 12)
            .Encounter(16, "route-1-area", "red", "walk", 30, 2, 5)
            .Encounter(16, "route-1-area", "blue", "walk", 30, 2, 5)
            .Encounter(16, "route-1-area", "game-2", "walk", 30, 2, 5)
            .Build();

        var rows = SnapshotBuilder.Build(raw).Availability;

        Assert.True(rows.Single(r => r.FormId == 23).Exclusive);
        Assert.All(rows.Where(r => r.FormId == 16), r => Assert.False(r.Exclusive));
    }

    [Fact]
    public void Evolution_conditions_keep_set_values_and_reduce_resources_to_their_slug()
    {
        var detail = System.Text.Json.JsonSerializer.Deserialize<EvolutionDetail>(
            """{"trigger":{"name":"use-item","url":"x"},"item":{"name":"thunder-stone","url":"x"},"min_level":null,"needs_overworld_rain":false,"time_of_day":""}""",
            PokeApiClient.JsonOptions)!;
        var link = (int species, string name, EvolutionDetail[] details, ChainLink[] next) => new ChainLink
        {
            Species = Fake.Ref("pokemon-species", species, name),
            EvolutionDetails = [.. details],
            EvolvesTo = [.. next],
        };
        var raw = new Fake()
            .Chain(new EvolutionChain { Id = 10, Chain = link(25, "pikachu", [], [link(26, "raichu", [detail], [])]) })
            .Build();

        var evolution = Assert.Single(SnapshotBuilder.Build(raw).Evolutions);

        Assert.Equal((25, 26, "use-item"), (evolution.FromSpeciesId, evolution.ToSpeciesId, evolution.Trigger));
        Assert.Equal(["item"], evolution.Conditions.Keys);
        Assert.Equal("thunder-stone", evolution.Conditions["item"].GetString());
    }
}
