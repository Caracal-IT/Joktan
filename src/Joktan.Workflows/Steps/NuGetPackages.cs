using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Joktan.Workflows.Engine;

namespace Joktan.Workflows.Steps;

/// <summary>
/// Fetches NuGet packages for C# scripts, with the packages they depend on, from feeds or folders of .nupkg files,
/// and finds the assemblies of each that suit this .NET. A package's latest version is the highest any feed has, and
/// a version comes from the first feed that has it. Packages are kept, so each version is fetched once, and those the
/// runtime already has are left out.
/// </summary>
public sealed class NuGetPackages(IReadOnlyList<string> feeds, string cacheFolder)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    // Packages that only describe the platform, which this runtime is.
    private static readonly HashSet<string> Platform = new(StringComparer.OrdinalIgnoreCase)
    {
        "NETStandard.Library", "Microsoft.NETCore.Platforms", "Microsoft.NETCore.Targets",
    };

    private static bool IsFolder(string feed) => !feed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string Base(string feed) => feed.EndsWith('/') ? feed : feed + "/";

    // A folder feed's .nupkg files, with ~ for the home folder.
    private static string FolderOf(string feed)
        => Path.GetFullPath(feed.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), feed[1..].TrimStart('/', '\\')) : feed);

    /// <summary>A NuGet package as written in a script step: its id, and the version, or null for the latest.</summary>
    public static (string Id, string? Version) Parse(string library)
    {
        var parts = library.Trim().Split([' ', '@', '/'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    /// <summary>The assemblies of the packages and all they depend on, by path.</summary>
    public async Task<IReadOnlyList<string>> RestoreAsync(IEnumerable<(string Id, string? Version)> packages, CancellationToken cancel)
    {
        var chosen = new Dictionary<string, (string Version, string Folder)>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Id, string? Version)>(packages);
        while (queue.Count > 0)
        {
            var (id, wanted) = queue.Dequeue();
            if (Platform.Contains(id) || IsInRuntime(id))
                continue;
            var version = wanted ?? await LatestAsync(id, cancel);

            // Of two versions asked for, the higher is used, as NuGet does.
            if (chosen.TryGetValue(id, out var known) && Compare(known.Version, version) >= 0)
                continue;
            var folder = await FetchAsync(id, version, cancel);
            chosen[id] = (version, folder);
            foreach (var dependency in Dependencies(folder))
                queue.Enqueue(dependency);
        }

        return chosen.Values.SelectMany(p => Assemblies(p.Folder)).ToList();
    }

    private static bool IsInRuntime(string id) => File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), id + ".dll"));

    private async Task<string> LatestAsync(string id, CancellationToken cancel)
    {
        var versions = new List<string>();
        foreach (var feed in feeds)
            versions.AddRange(await VersionsAsync(feed, id, cancel));
        return versions.Where(v => !v.Contains('-')).OrderBy(v => v, Comparer<string>.Create(Compare)).LastOrDefault()
               ?? throw new StepFailure($"No NuGet feed has a released version of {id}.");
    }

    private static async Task<IEnumerable<string>> VersionsAsync(string feed, string id, CancellationToken cancel)
    {
        if (IsFolder(feed))
        {
            var folder = FolderOf(feed);
            var prefix = id + ".";
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.nupkg").Select(Path.GetFileNameWithoutExtension)
                    .Where(n => n!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.Length > prefix.Length && char.IsDigit(n[prefix.Length]))
                    .Select(n => n![prefix.Length..]).ToList()
                : [];
        }

        using var response = await Http.GetAsync($"{Base(feed)}{id.ToLowerInvariant()}/index.json", cancel);
        if (!response.IsSuccessStatusCode)
            return [];
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return document.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToList();
    }

    // The package from the first feed that has it.
    private static async Task<byte[]?> DownloadAsync(string feed, string id, string version, CancellationToken cancel)
    {
        if (IsFolder(feed))
        {
            var folder = FolderOf(feed);
            var file = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.nupkg").FirstOrDefault(f => string.Equals(Path.GetFileName(f), $"{id}.{version}.nupkg", StringComparison.OrdinalIgnoreCase))
                : null;
            return file is null ? null : await File.ReadAllBytesAsync(file, cancel);
        }

        var name = $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}";
        using var response = await Http.GetAsync($"{Base(feed)}{id.ToLowerInvariant()}/{version.ToLowerInvariant()}/{name}.nupkg", cancel);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(cancel) : null;
    }

    private async Task<string> FetchAsync(string id, string version, CancellationToken cancel)
    {
        var folder = Path.Combine(cacheFolder, id.ToLowerInvariant(), version.ToLowerInvariant());
        var done = Path.Combine(folder, ".complete");
        if (File.Exists(done))
            return folder;

        byte[]? package = null;
        foreach (var feed in feeds)
        {
            package = await DownloadAsync(feed, id, version, cancel);
            if (package is not null)
                break;
        }

        if (package is null)
            throw new StepFailure($"No NuGet feed has {id} {version}.");

        // Unpacked beside the folder and then moved in, so a half-unpacked package is never used.
        var temporary = folder + ".tmp-" + Guid.NewGuid().ToString("N")[..6];
        using (var archive = new ZipArchive(new MemoryStream(package)))
            archive.ExtractToDirectory(temporary, overwriteFiles: true);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        Directory.Move(temporary, folder);
        await File.WriteAllTextAsync(done, "", cancel);
        return folder;
    }

    /// <summary>The packages a package depends on, for the group of its .nuspec that suits this .NET best.</summary>
    public static IEnumerable<(string Id, string? Version)> Dependencies(string folder)
    {
        var nuspec = Directory.EnumerateFiles(folder, "*.nuspec").FirstOrDefault();
        if (nuspec is null)
            return [];
        var root = XDocument.Load(nuspec).Root!;
        var dependencies = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependencies is null)
            return [];

        var groups = dependencies.Elements().Where(e => e.Name.LocalName == "group").ToList();
        IEnumerable<XElement> chosen;
        if (groups.Count == 0)
            chosen = dependencies.Elements();
        else
        {
            var best = groups.Select(g => (Group: g, Rank: Rank((string?)g.Attribute("targetFramework") ?? ""))).Where(g => g.Rank >= 0).MaxBy(g => g.Rank);
            chosen = best.Group?.Elements() ?? [];
        }

        return chosen.Where(e => e.Name.LocalName == "dependency" && (string?)e.Attribute("id") is { Length: > 0 })
            .Select(e => ((string)e.Attribute("id")!, Lowest((string?)e.Attribute("version"))))
            .ToList();
    }

    /// <summary>The assemblies of a package for the target that suits this .NET best.</summary>
    public static IEnumerable<string> Assemblies(string folder)
    {
        var lib = Path.Combine(folder, "lib");
        if (!Directory.Exists(lib))
            return [];
        var loose = Directory.EnumerateFiles(lib, "*.dll").ToList();
        var best = Directory.EnumerateDirectories(lib).Select(d => (Folder: d, Rank: Rank(Path.GetFileName(d)))).Where(d => d.Rank >= 0).MaxBy(d => d.Rank);
        return best.Folder is { } chosen ? Directory.EnumerateFiles(chosen, "*.dll") : loose;
    }

    /// <summary>How well a target framework suits this .NET, highest best; below zero when it does not suit it at all.</summary>
    public static int Rank(string framework)
    {
        var name = framework.Trim().ToLowerInvariant().TrimStart('.');
        if (name.Length == 0)
            return 100;
        if (name.StartsWith("netstandard", StringComparison.Ordinal))
            return Number(name["netstandard".Length..]) is { } standard && standard <= 2.1 ? 200 + (int)(standard * 10) : -1;
        if (name.StartsWith("netcoreapp", StringComparison.Ordinal))
            return Number(name["netcoreapp".Length..]) is { } core ? 400 + (int)(core * 10) : -1;

        // net5.0 and later have a dot; net462 and the like are the .NET Framework.
        if (name.StartsWith("net", StringComparison.Ordinal) && name.Contains('.'))
            return Number(name[3..].Split('-')[0]) is { } net && net <= Environment.Version.Major ? 1000 + (int)(net * 10) : -1;
        return -1;
    }

    private static double? Number(string text)
        => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    // A range such as [1.2.0, ) asks for at least 1.2.0, which NuGet takes.
    private static string? Lowest(string? range)
    {
        var text = range?.Trim().Trim('[', '(', ']', ')').Split(',')[0].Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Compares versions by their numbers, a release after its previews.</summary>
    public static int Compare(string? a, string? b)
    {
        static (Version Number, string Label) Split(string? v)
        {
            var text = v ?? "0";
            var dash = text.IndexOf('-');
            var number = dash < 0 ? text : text[..dash];
            return (Version.TryParse(number.Contains('.') ? number : number + ".0", out var parsed) ? parsed : new Version(0, 0), dash < 0 ? "" : text[(dash + 1)..]);
        }

        var (x, y) = (Split(a), Split(b));
        var byNumber = x.Number.CompareTo(y.Number);
        if (byNumber != 0)
            return byNumber;
        return (x.Label.Length, y.Label.Length) switch
        {
            (0, 0) => 0,
            (0, _) => 1,
            (_, 0) => -1,
            _ => string.CompareOrdinal(x.Label, y.Label),
        };
    }
}
