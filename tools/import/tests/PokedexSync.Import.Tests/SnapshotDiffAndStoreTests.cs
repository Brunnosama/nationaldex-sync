using System.Security.Cryptography;
using PokedexSync.Import.Snapshots;

namespace PokedexSync.Import.Tests;

public sealed class SnapshotDiffAndStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pokedex-sync-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static Snapshot Kanto(bool withAlolanRaichu = false, int pidgeyChance = 30)
    {
        var fake = new Fake()
            .Species(16, "pidgey", 1)
            .Pokemon(16, 16, "pidgey", isDefault: true)
            .Species(26, "raichu", 1)
            .Pokemon(26, 26, "raichu", isDefault: true)
            .Encounter(16, "route-1-area", "red", "walk", pidgeyChance, 2, 5);
        if (withAlolanRaichu)
            fake.Pokemon(26, 10100, "raichu-alola", isDefault: false, new FormSpec(10100, "raichu-alola", Generation: 7));
        return SnapshotBuilder.Build(fake.Build());
    }

    [Fact]
    public void Same_data_twice_has_no_changes()
    {
        var diff = SnapshotDiff.Compare(Kanto(), "v1", Kanto(), "v2");

        Assert.False(diff.HasChanges);
        Assert.Contains("No changes", SnapshotDiff.ToMarkdown(diff, Kanto()));
    }

    [Fact]
    public void New_and_removed_entries_are_reported()
    {
        var added = SnapshotDiff.Compare(Kanto(), "v1", Kanto(withAlolanRaichu: true), "v2");
        var removed = SnapshotDiff.Compare(Kanto(withAlolanRaichu: true), "v1", Kanto(), "v2");

        Assert.Equal(["26-10100-x-n", "26-10100-x-s"], added.EntriesAdded.Select(e => e.Id));
        Assert.Equal(["26-10100-x-n", "26-10100-x-s"], removed.EntriesRemoved.Select(e => e.Id));
        Assert.Empty(added.EntriesChanged);
    }

    [Fact]
    public void Changed_availability_is_reported_per_form_and_game()
    {
        var diff = SnapshotDiff.Compare(Kanto(pidgeyChance: 30), "v1", Kanto(pidgeyChance: 45), "v2");

        var change = Assert.Single(diff.AvailabilityChanges);
        Assert.Equal((16, "red", "changed"), (change.FormId, change.Game, change.Kind));
    }

    [Fact]
    public void Baseline_report_has_no_previous_version()
    {
        var diff = SnapshotDiff.Compare(null, null, Kanto(), "v1");

        Assert.Null(diff.OldVersion);
        Assert.StartsWith("# Snapshot v1 — baseline", SnapshotDiff.ToMarkdown(diff, Kanto()));
    }

    [Fact]
    public void Written_snapshot_reads_back_identically_and_manifest_hashes_match_the_files()
    {
        var snapshot = Kanto(withAlolanRaichu: true);
        var manifest = SnapshotStore.Write(_dir, "v1", snapshot, DateTimeOffset.UnixEpoch, "test");

        var read = SnapshotStore.Read(_dir);

        Assert.False(SnapshotDiff.Compare(snapshot, "a", read, "b").HasChanges);
        Assert.Equal(snapshot.Entries.Count, manifest.Counts.Entries);
        foreach (var (file, hash) in manifest.Files)
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(_dir, file)))));
    }

    [Fact]
    public void Entries_per_game_counts_every_entry_of_a_form_listed_in_that_game()
    {
        var perGame = SnapshotStore.EntriesPerGame(Kanto());

        Assert.Equal(2, perGame["red"]);
        Assert.Equal(0, perGame["blue"]);
    }
}
