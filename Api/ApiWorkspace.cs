using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Joktan.Mqtt;

namespace Joktan.Api;

/// <summary>
/// A folder of saved requests. Its folders are the folders in the tree, and each request is a
/// <c>.request.json</c> file named after the request, so the workspace reads well in a file
/// manager and in version control. The environments are in <c>environments.json</c> at the top.
/// </summary>
public sealed partial class ApiWorkspace(string folder)
{
    public const string RequestExtension = ".request.json";
    public const string EnvironmentsFile = "environments.json";

    public string Folder { get; } = Path.GetFullPath(folder);

    public static string DefaultFolder => Path.Combine(DataFolder.Path, "api");

    // Remembers the chosen folder; it cannot live in the workspace itself.
    private static string LocationPath => Path.Combine(DataFolder.Path, "api-location.json");

    /// <summary>The folder chosen for the workspace, or the default.</summary>
    public static string ChosenFolder
    {
        get
        {
            try
            {
                if (File.Exists(LocationPath)
                    && JsonSerializer.Deserialize<LocationFile>(File.ReadAllText(LocationPath)) is { Folder.Length: > 0 } location)
                    return location.Folder;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // An unreadable choice falls back to the default folder.
            }

            return DefaultFolder;
        }
    }

    public static void SaveChosenFolder(string folder)
    {
        try
        {
            if (SameFolder(folder, DefaultFolder))
            {
                File.Delete(LocationPath);
                return;
            }

            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(LocationPath, JsonSerializer.Serialize(new LocationFile { Folder = folder }, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A choice that cannot be saved is used until the app closes.
        }
    }

    public static bool SameFolder(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a folder holds requests or environments already.</summary>
    public static bool HasContent(string folder)
        => Directory.Exists(folder)
           && (File.Exists(Path.Combine(folder, EnvironmentsFile))
               || Directory.EnumerateFiles(folder, "*" + RequestExtension, SearchOption.AllDirectories).Any());

    /// <summary>The folders and requests below a folder of the workspace, folders first, each by name.</summary>
    public IEnumerable<string> Folders(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => Path.GetFileName(d), StringComparer.CurrentCultureIgnoreCase)
            : [];

    public IEnumerable<string> Requests(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*" + RequestExtension)
                .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)
            : [];

    /// <summary>A request's name: its file name without the extension.</summary>
    public static string NameOf(string requestPath)
        => Path.GetFileName(requestPath)[..^RequestExtension.Length];

    public static ApiRequest Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiRequest>(File.ReadAllText(path)) ?? new ApiRequest();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ApiRequest();
        }
    }

    public static void Write(string path, ApiRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(request, JsonFormats.Indented));
    }

    public ApiEnvironments ReadEnvironments()
    {
        try
        {
            var path = Path.Combine(Folder, EnvironmentsFile);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ApiEnvironments>(File.ReadAllText(path)) ?? new ApiEnvironments();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable environments are treated as none.
        }

        return new ApiEnvironments();
    }

    public void WriteEnvironments(ApiEnvironments environments)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, EnvironmentsFile), JsonSerializer.Serialize(environments, JsonFormats.Indented));
    }

    /// <summary>A path in a folder for a new file or folder, numbered if the name is taken.</summary>
    public static string FreePath(string folder, string name, string extension)
    {
        name = SafeName(name);
        var path = Path.Combine(folder, name + extension);
        for (var n = 2; File.Exists(path) || Directory.Exists(path); n++)
            path = Path.Combine(folder, $"{name} {n}{extension}");
        return path;
    }

    /// <summary>A name that can be used as a file name everywhere.</summary>
    public static string SafeName(string name)
    {
        name = UnsafeCharacters().Replace(name.Trim(), "-").Trim('.', ' ');
        return name.Length == 0 ? "Untitled" : name;
    }

    /// <summary>Copies everything in the workspace into another folder; files already there are kept.</summary>
    public void CopyTo(string folder)
    {
        foreach (var source in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Folder, source);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.')))
                continue;
            var target = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
                File.Copy(source, target);
        }
    }

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1F]")]
    private static partial Regex UnsafeCharacters();

    private sealed class LocationFile
    {
        public string Folder { get; set; } = "";
    }
}
