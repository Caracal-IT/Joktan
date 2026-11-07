using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Core;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Engine;

/// <summary>
/// The pages built on Workflows / Pages, each a <c>&lt;name&gt;.page.json</c> file in a folder, which user
/// input steps show on the website. With no folder, as in tests, they are only kept in memory.
/// </summary>
public sealed class PageLibrary
{
    public const string Extension = ".page.json";

    private readonly Dictionary<string, (WorkflowPage Page, string? Path)> _pages = new(StringComparer.Ordinal);

    /// <param name="folder">Where the files are; null to keep the pages in memory alone.</param>
    public PageLibrary(string? folder)
    {
        Folder = folder is null ? null : Path.GetFullPath(folder);
        Reload();
    }

    public string? Folder { get; }

    /// <summary>Why the folder could not be read, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The pages, by name.</summary>
    public IReadOnlyList<WorkflowPage> All => _pages.Values.Select(p => p.Page).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Raised after a page is saved or deleted, or the folder is read again.</summary>
    public event Action? Changed;

    public WorkflowPage? Find(string idOrName)
        => _pages.TryGetValue(idOrName, out var found) ? found.Page
            : _pages.Values.Select(p => p.Page).FirstOrDefault(p => string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    public void Reload()
    {
        // Pages kept only in memory have nothing to read again.
        if (Folder is null)
            return;
        _pages.Clear();
        Error = null;
        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var path in Directory.EnumerateFiles(Folder, "*" + Extension))
                {
                    if (TryRead(path) is { } page && !_pages.ContainsKey(page.Id))
                        _pages[page.Id] = (page, path);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }

        Changed?.Invoke();
    }

    public static WorkflowPage? TryRead(string path)
    {
        try
        {
            var page = JsonSerializer.Deserialize<WorkflowPage>(File.ReadAllText(path));
            if (page is null)
                return null;
            if (page.Id.Length == 0)
                page.Id = WorkflowLibrary.NewId();
            if (page.Name.Length == 0)
                page.Name = Path.GetFileName(path)[..^Extension.Length];
            page.Items ??= [];
            foreach (var item in page.Items)
                item.Options ??= [];
            return page;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a page to its file, renaming the file when the page's name has changed.</summary>
    public void Save(WorkflowPage page)
    {
        var previous = _pages.TryGetValue(page.Id, out var known) ? known.Path : null;
        string? path = null;
        if (Folder is not null)
        {
            Directory.CreateDirectory(Folder);
            path = previous is not null && Path.GetFileName(previous) == FileName(page.Name) ? previous : FreePath(page.Name);
            File.WriteAllText(path, JsonSerializer.Serialize(page, JsonFormats.Indented));
            if (previous is not null && previous != path && File.Exists(previous))
                File.Delete(previous);
        }

        _pages[page.Id] = (page, path);
        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        if (!_pages.Remove(id, out var known))
            return;
        if (known.Path is not null && File.Exists(known.Path))
            File.Delete(known.Path);
        Changed?.Invoke();
    }

    /// <summary>A name not yet used by a page, such as "New page 2".</summary>
    public string FreeName(string name)
    {
        var candidate = name;
        for (var n = 2; _pages.Values.Any(p => string.Equals(p.Page.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    private string FreePath(string name)
    {
        var path = Path.Combine(Folder!, FileName(name));
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(Folder!, FileName($"{name} {n}"));
        return path;
    }

    private static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' ? '-' : c).ToArray());
        return (safe.Length == 0 ? "page" : safe) + Extension;
    }
}
