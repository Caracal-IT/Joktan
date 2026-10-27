using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Joktan.Mqtt;

namespace Joktan.Notes;

/// <summary>
/// The notes: an index of the pages and how they nest, and one Markdown file per page, all in one
/// folder. That is the <c>notes</c> folder of Joktan's data folder unless another has been chosen.
/// Writes happen on a background thread.
/// </summary>
public sealed class NoteStore
{
    private readonly SettingsWriter _writer;

    private NoteStore(string folder)
    {
        Folder = folder;
        _writer = new SettingsWriter((path, e) => Failed?.Invoke(path, e));
    }

    public string Folder { get; }

    public string IndexPath => Path.Combine(Folder, "index.json");

    public List<NotePage> Pages { get; private set; } = [];

    /// <summary>Whether there was no index yet, so notes have never been kept here.</summary>
    public bool IsNew { get; private set; }

    /// <summary>The page that was open last.</summary>
    public string Selected { get; set; } = "";

    /// <summary>Called on a background thread with the path of a file that could not be written.</summary>
    public event Action<string, Exception>? Failed;

    /// <summary>Where notes are kept when no other folder has been chosen.</summary>
    public static string DefaultFolder => Path.Combine(DataFolder.Path, "notes");

    // Remembers the chosen folder; it cannot live in the notes folder itself.
    private static string LocationPath => Path.Combine(DataFolder.Path, "notes-location.json");

    /// <summary>The folder chosen for notes, or the default.</summary>
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

    /// <summary>Remembers the folder for notes; the default folder is remembered as no choice.</summary>
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

    /// <summary>Whether a folder already holds notes.</summary>
    public static bool HasNotes(string folder) => File.Exists(Path.Combine(folder, "index.json"));

    public static NoteStore Load() => Load(ChosenFolder);

    public static NoteStore Load(string folder)
    {
        var store = new NoteStore(folder);
        store.IsNew = !File.Exists(store.IndexPath);
        try
        {
            if (!store.IsNew
                && JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(store.IndexPath)) is { } index)
            {
                store.Pages = Clean(index.Pages);
                store.Selected = index.Selected ?? "";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable index is treated as no notes; the page files are left where they are.
        }

        return store;
    }

    public string PagePath(string id) => Path.Combine(Folder, id + ".md");

    public string ReadText(string id)
    {
        try
        {
            var path = PagePath(id);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public void WriteText(string id, string text)
    {
        if (!EnsureFolder())
            return;
        _writer.Write(PagePath(id), text);
    }

    /// <summary>Writes the index: the pages, how they nest and their titles.</summary>
    public void SaveIndex(List<NotePage> pages, string selected)
    {
        Pages = pages;
        Selected = selected;
        IsNew = false;
        if (!EnsureFolder())
            return;
        var index = new IndexFile { Pages = pages, Selected = selected };
        _writer.Write(IndexPath, JsonSerializer.Serialize(index, JsonFormats.Indented));
    }

    /// <summary>Deletes the files of these pages. They should already be out of the index.</summary>
    public void DeleteText(IEnumerable<string> ids)
    {
        var paths = ids.Select(PagePath).ToList();
        Task.Run(async () =>
        {
            // A write still queued for one of them would otherwise put it back.
            await _writer.FlushAsync();
            foreach (var path in paths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Failed?.Invoke(path, e);
                }
            }
        });
    }

    /// <summary>
    /// Copies the index and the pages' files into another folder, once what is queued is on disk.
    /// Files already there are kept.
    /// </summary>
    public async Task CopyToAsync(string folder)
    {
        await _writer.FlushAsync();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(folder);
            var ids = Pages.SelectMany(Flatten).Select(p => p.Id);
            foreach (var name in ids.Select(id => id + ".md").Prepend("index.json"))
            {
                var source = Path.Combine(Folder, name);
                var target = Path.Combine(folder, name);
                if (File.Exists(source) && !File.Exists(target))
                    File.Copy(source, target);
            }
        });

        static IEnumerable<NotePage> Flatten(NotePage page) => page.Children.SelectMany(Flatten).Prepend(page);
    }

    /// <summary>Completes once everything written so far is on disk.</summary>
    public Task FlushAsync() => _writer.FlushAsync();

    private bool EnsureFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(Folder, e);
            return false;
        }
    }

    // Drops empty entries and gives each page an ID that is safe as a file name and used once.
    private static List<NotePage> Clean(List<NotePage>? pages, HashSet<string>? seen = null)
    {
        seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<NotePage>();
        foreach (var page in pages ?? [])
        {
            if (page is null)
                continue;
            if (string.IsNullOrEmpty(page.Id) || page.Id.Any(c => !char.IsAsciiLetterOrDigit(c)) || !seen.Add(page.Id))
                page.Id = NotePage.NewId();
            page.Title ??= "";
            page.Children = Clean(page.Children, seen);
            cleaned.Add(page);
        }

        return cleaned;
    }

    private sealed class LocationFile
    {
        public string Folder { get; set; } = "";
    }

    private sealed class IndexFile
    {
        public List<NotePage>? Pages { get; set; }

        public string? Selected { get; set; }
    }
}
