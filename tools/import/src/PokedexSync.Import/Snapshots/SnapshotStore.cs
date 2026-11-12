using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PokedexSync.Import.Snapshots;

/// <summary>
/// Reads and writes versioned snapshots under data/snapshots/vN. Each data file is a JSON array
/// with one record per line, so git diffs between versions stay readable.
/// </summary>
public static partial class SnapshotStore
{
    public const string ManifestFile = "manifest.json";
    public const string DiffReportFile = "DIFF.md";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions IndentedOptions = new(JsonOptions) { WriteIndented = true, NewLine = "\n" };
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    [GeneratedRegex(@"^v(\d+)$")]
    private static partial Regex VersionDirectory();

    /// <summary>Existing snapshot versions in ascending order, e.g. [1, 2].</summary>
    public static IReadOnlyList<int> ListVersions(string snapshotsRoot) =>
        Directory.Exists(snapshotsRoot)
            ? Directory.GetDirectories(snapshotsRoot)
                .Select(d => VersionDirectory().Match(Path.GetFileName(d)))
                .Where(m => m.Success && File.Exists(Path.Combine(snapshotsRoot, m.Value, ManifestFile)))
                .Select(m => int.Parse(m.Groups[1].Value))
                .Order()
                .ToList()
            : [];

    public static string VersionName(int version) => $"v{version}";

    public static Manifest Write(string directory, string version, Snapshot snapshot, DateTimeOffset importedAt, string source)
    {
        Directory.CreateDirectory(directory);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["games.json"] = WriteRecords(directory, "games.json", snapshot.Games),
            ["boxes.json"] = WriteRecords(directory, "boxes.json", snapshot.Boxes),
            ["species.json"] = WriteRecords(directory, "species.json", snapshot.Species),
            ["entries.json"] = WriteRecords(directory, "entries.json", snapshot.Entries),
            ["availability.json"] = WriteRecords(directory, "availability.json", snapshot.Availability),
            ["evolutions.json"] = WriteRecords(directory, "evolutions.json", snapshot.Evolutions),
        };

        var manifest = new Manifest
        {
            Version = version,
            ImportedAt = importedAt,
            Source = source,
            Counts = CountsOf(snapshot),
            EntriesPerGame = EntriesPerGame(snapshot),
            Files = files,
        };
        File.WriteAllText(Path.Combine(directory, ManifestFile), JsonSerializer.Serialize(manifest, IndentedOptions) + "\n", Utf8NoBom);
        return manifest;
    }

    public static Snapshot Read(string directory) => new()
    {
        Games = ReadRecords<Game>(directory, "games.json"),
        Boxes = ReadRecords<RegionBox>(directory, "boxes.json"),
        Species = ReadRecords<SpeciesRecord>(directory, "species.json"),
        Entries = ReadRecords<DexEntry>(directory, "entries.json"),
        Availability = ReadRecords<AvailabilityRow>(directory, "availability.json"),
        Evolutions = ReadRecords<EvolutionRecord>(directory, "evolutions.json"),
    };

    public static ManifestCounts CountsOf(Snapshot s) => new(
        s.Games.Count,
        s.Boxes.Count,
        s.Species.Count,
        s.Entries.Select(e => e.FormId).Distinct().Count(),
        s.Entries.Count,
        s.Availability.Count,
        s.Evolutions.Count);

    /// <summary>Entries shown in each game's view: those whose form PokeAPI lists in that game.</summary>
    public static SortedDictionary<string, int> EntriesPerGame(Snapshot s)
    {
        var entriesPerForm = s.Entries.GroupBy(e => e.FormId).ToDictionary(g => g.Key, g => g.Count());
        var perGame = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var game in s.Games)
            perGame[game.Id] = 0;
        foreach (var (game, formId) in s.Availability.Select(r => (r.Game, r.FormId)).Distinct())
            perGame[game] += entriesPerForm[formId];
        return perGame;
    }

    private static string WriteRecords<T>(string directory, string fileName, IEnumerable<T> records)
    {
        var sb = new StringBuilder("[\n");
        var first = true;
        foreach (var record in records)
        {
            if (!first)
                sb.Append(",\n");
            sb.Append(JsonSerializer.Serialize(record, JsonOptions));
            first = false;
        }
        sb.Append("\n]\n");

        var bytes = Utf8NoBom.GetBytes(sb.ToString());
        File.WriteAllBytes(Path.Combine(directory, fileName), bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static List<T> ReadRecords<T>(string directory, string fileName) =>
        JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path.Combine(directory, fileName)), JsonOptions)
            ?? throw new InvalidDataException($"{fileName} in {directory} is empty.");
}
