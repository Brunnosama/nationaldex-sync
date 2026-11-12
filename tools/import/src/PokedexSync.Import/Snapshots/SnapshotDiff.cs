using System.Text;
using System.Text.Json;

namespace PokedexSync.Import.Snapshots;

public sealed record EntryChange(string Id, string Field, string? Before, string? After);

/// <summary>A form starting, stopping or changing how it appears in one game.</summary>
public sealed record AvailabilityChange(int FormId, string FormName, string Game, string Kind);

public sealed record SnapshotDiffResult
{
    public required string? OldVersion { get; init; }
    public required string NewVersion { get; init; }
    public required ManifestCounts? OldCounts { get; init; }
    public required ManifestCounts NewCounts { get; init; }
    public required IReadOnlyList<string> GamesAdded { get; init; }
    public required IReadOnlyList<string> GamesRemoved { get; init; }
    public required IReadOnlyList<DexEntry> EntriesAdded { get; init; }
    public required IReadOnlyList<DexEntry> EntriesRemoved { get; init; }
    public required IReadOnlyList<EntryChange> EntriesChanged { get; init; }
    public required IReadOnlyList<AvailabilityChange> AvailabilityChanges { get; init; }
    public required int EvolutionsAdded { get; init; }
    public required int EvolutionsRemoved { get; init; }

    public bool HasChanges =>
        GamesAdded.Count + GamesRemoved.Count + EntriesAdded.Count + EntriesRemoved.Count
        + EntriesChanged.Count + AvailabilityChanges.Count + EvolutionsAdded + EvolutionsRemoved > 0;
}

/// <summary>
/// Compares a new snapshot with the previous one so a reviewer can see what an import changed:
/// new and removed entries, changed entry fields, and changed availability.
/// </summary>
public static class SnapshotDiff
{
    private const int MaxListed = 300;

    public static SnapshotDiffResult Compare(Snapshot? oldSnapshot, string? oldVersion, Snapshot newSnapshot, string newVersion)
    {
        var old = oldSnapshot ?? new Snapshot { Games = [], Boxes = [], Species = [], Entries = [], Availability = [], Evolutions = [] };

        var oldEntries = old.Entries.ToDictionary(e => e.Id);
        var newEntries = newSnapshot.Entries.ToDictionary(e => e.Id);
        var oldGames = old.Games.Select(g => g.Id).ToHashSet();
        var newGames = newSnapshot.Games.Select(g => g.Id).ToHashSet();

        var changed = new List<EntryChange>();
        foreach (var (id, after) in newEntries)
        {
            if (!oldEntries.TryGetValue(id, out var before))
                continue;
            // SortIndex is left out: inserting one entry shifts every entry after it.
            Compare(changed, id, nameof(DexEntry.Name), before.Name, after.Name);
            Compare(changed, id, nameof(DexEntry.DisplayName), before.DisplayName, after.DisplayName);
            Compare(changed, id, nameof(DexEntry.FormDisplayName), before.FormDisplayName, after.FormDisplayName);
            Compare(changed, id, nameof(DexEntry.IntroductionGeneration), before.IntroductionGeneration.ToString(), after.IntroductionGeneration.ToString());
            Compare(changed, id, nameof(DexEntry.RegionBox), before.RegionBox, after.RegionBox);
            Compare(changed, id, nameof(DexEntry.IsDefaultForm), before.IsDefaultForm.ToString(), after.IsDefaultForm.ToString());
            Compare(changed, id, nameof(DexEntry.HomeSprite), before.HomeSprite, after.HomeSprite);
        }

        return new SnapshotDiffResult
        {
            OldVersion = oldSnapshot is null ? null : oldVersion,
            NewVersion = newVersion,
            OldCounts = oldSnapshot is null ? null : SnapshotStore.CountsOf(oldSnapshot),
            NewCounts = SnapshotStore.CountsOf(newSnapshot),
            GamesAdded = newGames.Except(oldGames).Order(StringComparer.Ordinal).ToList(),
            GamesRemoved = oldGames.Except(newGames).Order(StringComparer.Ordinal).ToList(),
            EntriesAdded = newSnapshot.Entries.Where(e => !oldEntries.ContainsKey(e.Id)).ToList(),
            EntriesRemoved = old.Entries.Where(e => !newEntries.ContainsKey(e.Id)).ToList(),
            EntriesChanged = changed,
            AvailabilityChanges = CompareAvailability(old, newSnapshot),
            EvolutionsAdded = CountMissing(newSnapshot.Evolutions, old.Evolutions),
            EvolutionsRemoved = CountMissing(old.Evolutions, newSnapshot.Evolutions),
        };
    }

    private static void Compare(List<EntryChange> changes, string id, string field, string? before, string? after)
    {
        if (before != after)
            changes.Add(new EntryChange(id, field, before, after));
    }

    private static List<AvailabilityChange> CompareAvailability(Snapshot old, Snapshot current)
    {
        static Dictionary<(int FormId, string Game), string> Groups(Snapshot s) =>
            s.Availability
                .GroupBy(r => (r.FormId, r.Game))
                .ToDictionary(g => g.Key, g => string.Join('\n', g.Select(r => JsonSerializer.Serialize(r, SnapshotStore.JsonOptions)).Order(StringComparer.Ordinal)));

        var formNames = old.Entries.Concat(current.Entries)
            .GroupBy(e => e.FormId)
            .ToDictionary(g => g.Key, g => g.Last().Name);
        var before = Groups(old);
        var after = Groups(current);

        var changes = new List<AvailabilityChange>();
        foreach (var key in before.Keys.Union(after.Keys).OrderBy(k => k.FormId).ThenBy(k => k.Game, StringComparer.Ordinal))
        {
            var kind = (before.TryGetValue(key, out var b), after.TryGetValue(key, out var a)) switch
            {
                (false, true) => "added",
                (true, false) => "removed",
                _ when a != b => "changed",
                _ => null,
            };
            if (kind is not null)
                changes.Add(new AvailabilityChange(key.FormId, formNames.GetValueOrDefault(key.FormId, key.FormId.ToString()), key.Game, kind));
        }
        return changes;
    }

    private static int CountMissing(IEnumerable<EvolutionRecord> from, IEnumerable<EvolutionRecord> other)
    {
        static string Key(EvolutionRecord r) => JsonSerializer.Serialize(r, SnapshotStore.JsonOptions);
        var otherKeys = other.Select(Key).ToHashSet();
        return from.Count(r => !otherKeys.Contains(Key(r)));
    }

    public static string ToMarkdown(SnapshotDiffResult diff, Snapshot newSnapshot)
    {
        var md = new StringBuilder();
        md.AppendLine(diff.OldVersion is null
            ? $"# Snapshot {diff.NewVersion} — baseline"
            : $"# Snapshot diff: {diff.OldVersion} → {diff.NewVersion}");
        md.AppendLine();
        md.AppendLine(diff.OldVersion is null
            ? "No previous snapshot to compare with, so every entry is new. Counts below describe the whole snapshot."
            : diff.HasChanges ? "Changes found against the previous snapshot." : "No changes against the previous snapshot.");
        md.AppendLine();

        md.AppendLine("## Counts");
        md.AppendLine();
        md.AppendLine("| | Before | After | Change |");
        md.AppendLine("| --- | ---: | ---: | ---: |");
        var o = diff.OldCounts;
        var n = diff.NewCounts;
        CountRow(md, "Games", o?.Games, n.Games);
        CountRow(md, "Region boxes", o?.Boxes, n.Boxes);
        CountRow(md, "Species", o?.Species, n.Species);
        CountRow(md, "Forms", o?.Forms, n.Forms);
        CountRow(md, "Entries", o?.Entries, n.Entries);
        CountRow(md, "Availability rows", o?.AvailabilityRows, n.AvailabilityRows);
        CountRow(md, "Evolutions", o?.Evolutions, n.Evolutions);
        md.AppendLine();

        if (diff.OldVersion is null)
        {
            AppendBaseline(md, newSnapshot);
        }
        else
        {
            AppendList(md, "Games added", diff.GamesAdded);
            AppendList(md, "Games removed", diff.GamesRemoved);
            AppendList(md, "Entries added", diff.EntriesAdded.Select(e => $"`{e.Id}` {Describe(e)}").ToList());
            AppendList(md, "Entries removed (user progress for these is kept and flagged, never deleted)",
                diff.EntriesRemoved.Select(e => $"`{e.Id}` {Describe(e)}").ToList());
            AppendList(md, "Entry fields changed", diff.EntriesChanged.Select(c => $"`{c.Id}` {c.Field}: `{c.Before}` → `{c.After}`").ToList());
            AppendList(md, "Availability changed", diff.AvailabilityChanges.Select(c => $"{c.Kind}: {c.FormName} (form {c.FormId}) in {c.Game}").ToList());
            md.AppendLine($"Evolutions: {diff.EvolutionsAdded} added, {diff.EvolutionsRemoved} removed.");
            md.AppendLine();
        }

        AppendReviewNotes(md, newSnapshot);
        // LF on every OS, like the data files, so the report is the same wherever it is generated.
        return md.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendBaseline(StringBuilder md, Snapshot s)
    {
        md.AppendLine("## Entries per region box");
        md.AppendLine();
        md.AppendLine("| Box | Species | Forms | Entries |");
        md.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var box in s.Boxes)
        {
            var entries = s.Entries.Where(e => e.RegionBox == box.Id).ToList();
            md.AppendLine($"| {box.DisplayName} | {entries.Select(e => e.SpeciesId).Distinct().Count()} | {entries.Select(e => e.FormId).Distinct().Count()} | {entries.Count} |");
        }
        md.AppendLine();

        md.AppendLine("## Entries per game");
        md.AppendLine();
        md.AppendLine("Entries whose form PokeAPI lists at least one encounter for in that game.");
        md.AppendLine();
        md.AppendLine("| Game | Generation | Entries | Exclusive forms |");
        md.AppendLine("| --- | ---: | ---: | ---: |");
        var perGame = SnapshotStore.EntriesPerGame(s);
        foreach (var game in s.Games)
        {
            var exclusives = s.Availability.Where(r => r.Game == game.Id && r.Exclusive).Select(r => r.FormId).Distinct().Count();
            md.AppendLine($"| {game.DisplayName} | {game.Generation} | {perGame[game.Id]} | {exclusives} |");
        }
        md.AppendLine();
    }

    /// <summary>Things a reviewer should look at before merging, whatever changed.</summary>
    private static void AppendReviewNotes(StringBuilder md, Snapshot s)
    {
        md.AppendLine("## Review notes");
        md.AppendLine();
        var noSprite = s.Entries.Where(e => e is { Shiny: false, Gender: null or Gender.Male } && e.HomeSprite is null).ToList();
        AppendList(md, "Forms without a HOME sprite (permanent in PokeAPI, but possibly not stored in HOME)",
            noSprite.Select(e => $"`{e.Name}` (form {e.FormId})").ToList());

        var withEncounters = s.Availability.Select(r => r.FormId).ToHashSet();
        var forms = s.Entries.Select(e => e.FormId).Distinct().Count();
        md.AppendLine($"Forms with no encounter in any game: {forms - withEncounters.Count} of {forms}. They appear in the National Dex checklist but in no game view.");
        md.AppendLine();
    }

    private static string Describe(DexEntry e) =>
        $"{e.DisplayName}{e.Gender switch { Gender.Male => " ♂", Gender.Female => " ♀", _ => "" }}{(e.Shiny ? " (Shiny)" : "")}";

    private static void CountRow(StringBuilder md, string label, int? before, int after) =>
        md.AppendLine($"| {label} | {(before?.ToString() ?? "—")} | {after} | {(before is null ? "—" : (after - before.Value).ToString("+0;-0;0"))} |");

    private static void AppendList(StringBuilder md, string title, IReadOnlyList<string> items)
    {
        md.AppendLine($"### {title} ({items.Count})");
        md.AppendLine();
        if (items.Count == 0)
        {
            md.AppendLine("None.");
        }
        else
        {
            foreach (var item in items.Take(MaxListed))
                md.AppendLine($"- {item}");
            if (items.Count > MaxListed)
                md.AppendLine($"- … and {items.Count - MaxListed} more");
        }
        md.AppendLine();
    }
}
