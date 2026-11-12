using System.Diagnostics;
using PokedexSync.Import.PokeApi;
using PokedexSync.Import.Snapshots;

namespace PokedexSync.Import;

public static class Program
{
    private const string Usage = """
        Usage:
          import [--dry-run] [--delay-ms 150] [--species-limit N] [--allow-any-branch]
              Fetch from PokeAPI, build the next snapshot data/snapshots/vN and its DIFF.md.
              --dry-run          Build and print the diff report against the latest snapshot; write nothing.
              --delay-ms         Minimum gap between PokeAPI requests (default 150). Cached responses skip it.
              --species-limit    Only the first N species (dry runs only, for quick local checks).
              --allow-any-branch Skip the check that a real import runs on a data/* branch.
          diff <old-snapshot-dir> <new-snapshot-dir>
              Print the diff report between two existing snapshots.
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "import" => await ImportAsync(args[1..]),
                "diff" when args.Length == 3 => Diff(args[1], args[2]),
                _ => Fail(Usage),
            };
        }
        catch (PokeApiFormatException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine("The import stopped because a field it uses has changed. Update the PokeAPI models and rules before importing.");
            return 2;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ImportAsync(string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        var allowAnyBranch = args.Contains("--allow-any-branch");
        var delayMs = IntOption(args, "--delay-ms") ?? 150;
        var speciesLimit = IntOption(args, "--species-limit");
        if (speciesLimit is not null && !dryRun)
            return Fail("--species-limit only works with --dry-run: a partial snapshot must never be released.");

        var repo = FindRepoRoot();
        var snapshotsRoot = Path.Combine(repo, "data", "snapshots");
        var cacheDir = Path.Combine(repo, "tools", "import", ".cache");

        if (!dryRun && !allowAnyBranch)
        {
            var branch = CurrentBranch(repo);
            if (branch is null || !branch.StartsWith("data/", StringComparison.Ordinal))
                return Fail($"Imports run on their own data/* branch (current: {branch ?? "unknown"}). Use --dry-run to only preview.");
        }

        using var client = new PokeApiClient(cacheDir, TimeSpan.FromMilliseconds(delayMs), Console.Out);
        var started = Stopwatch.StartNew();
        var raw = await new PokeApiFetcher(client, Console.Out).FetchAsync(speciesLimit);
        Console.WriteLine($"Fetched in {started.Elapsed:mm\\:ss} ({client.NetworkRequests} requests to PokeAPI, {client.CacheHits} from cache).");

        var snapshot = SnapshotBuilder.Build(raw);

        var versions = SnapshotStore.ListVersions(snapshotsRoot);
        var previousVersion = versions.Count > 0 ? SnapshotStore.VersionName(versions[^1]) : null;
        var previous = previousVersion is null ? null : SnapshotStore.Read(Path.Combine(snapshotsRoot, previousVersion));
        var nextVersion = SnapshotStore.VersionName(versions.Count > 0 ? versions[^1] + 1 : 1);

        var diff = SnapshotDiff.Compare(previous, previousVersion, snapshot, dryRun ? $"{nextVersion} (dry run)" : nextVersion);
        var report = SnapshotDiff.ToMarkdown(diff, snapshot);

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine(report);
            return 0;
        }

        var directory = Path.Combine(snapshotsRoot, nextVersion);
        if (Directory.Exists(directory))
            return Fail($"{directory} already exists.");

        var manifest = SnapshotStore.Write(directory, nextVersion, snapshot, DateTimeOffset.UtcNow, PokeApiClient.BaseUrl);
        await File.WriteAllTextAsync(Path.Combine(directory, SnapshotStore.DiffReportFile), report);
        Console.WriteLine($"Wrote {Path.GetRelativePath(repo, directory)}: {manifest.Counts.Entries} entries, {manifest.Counts.Forms} forms, {manifest.Counts.Games} games.");
        Console.WriteLine($"Diff report: {Path.GetRelativePath(repo, Path.Combine(directory, SnapshotStore.DiffReportFile))}");
        return 0;
    }

    private static int Diff(string oldDir, string newDir)
    {
        var newSnapshot = SnapshotStore.Read(newDir);
        var diff = SnapshotDiff.Compare(SnapshotStore.Read(oldDir), Path.GetFileName(Path.TrimEndingDirectorySeparator(oldDir)),
            newSnapshot, Path.GetFileName(Path.TrimEndingDirectorySeparator(newDir)));
        Console.WriteLine(SnapshotDiff.ToMarkdown(diff, newSnapshot));
        return 0;
    }

    private static int? IntOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var value) ? value : null;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Run the import from inside the repository.");
    }

    private static string? CurrentBranch(string repo)
    {
        using var git = Process.Start(new ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        if (git is null)
            return null;
        var output = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        return git.ExitCode == 0 ? output : null;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
