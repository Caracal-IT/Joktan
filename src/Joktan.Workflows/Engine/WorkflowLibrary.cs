using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Core;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Engine;

/// <summary>
/// The workflows in a folder, each a <c>&lt;name&gt;.workflow.json</c> file, so that they can be kept with a
/// project and in version control. They may be put in folders below it, which keep them in order; a workflow's
/// folder is where its file is, written with / between levels, such as <c>Orders/Returns</c>.
/// </summary>
public sealed class WorkflowLibrary
{
    public const string Extension = ".workflow.json";

    private readonly Dictionary<string, (Workflow Workflow, string Path)> _workflows = new(StringComparer.Ordinal);

    public WorkflowLibrary(string folder)
    {
        Folder = Path.GetFullPath(folder);
        Reload();
    }

    public string Folder { get; private set; }

    /// <summary>Why the folder could not be read, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The workflows, by name.</summary>
    public IReadOnlyList<Workflow> All => _workflows.Values.Select(w => w.Workflow).OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Raised after a workflow is saved or deleted, or the folder is read again.</summary>
    public event Action? Changed;

    public Workflow? Find(string idOrName)
        => _workflows.TryGetValue(idOrName, out var found) ? found.Workflow
            : _workflows.Values.Select(w => w.Workflow).FirstOrDefault(w => string.Equals(w.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    public void UseFolder(string folder)
    {
        Folder = Path.GetFullPath(folder);
        Reload();
    }

    public void Reload()
    {
        _workflows.Clear();
        Error = null;
        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var path in Directory.EnumerateFiles(Folder, "*" + Extension, SearchOption.AllDirectories).Where(p => !IsHidden(p)))
                {
                    if (TryRead(path) is { } workflow && !_workflows.ContainsKey(workflow.Id))
                        _workflows[workflow.Id] = (workflow, path);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }

        Changed?.Invoke();
    }

    public static Workflow? TryRead(string path)
    {
        try
        {
            var workflow = JsonSerializer.Deserialize<Workflow>(File.ReadAllText(path));
            if (workflow is null)
                return null;
            if (workflow.Id.Length == 0)
                workflow.Id = NewId();
            if (workflow.Name.Length == 0)
                workflow.Name = Path.GetFileName(path)[..^Extension.Length];
            workflow.Steps ??= [];
            workflow.Links ??= [];
            workflow.Triggers ??= new();
            workflow.Triggers.WebhookPath ??= "";
            workflow.Globals ??= [];
            workflow.Samples ??= [];
            workflow.ErrorWorkflow ??= "";

            // The single sample input of a workflow saved before there could be several becomes its first sample.
            if (workflow.SampleInput is { } old)
            {
                if (workflow.Samples.Count == 0 && old.Trim() is { Length: > 0 } and not "{}")
                    workflow.Samples.Add(new NamedInput { Name = "Sample", Input = old });
                workflow.SampleInput = null;
            }
            return workflow;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a workflow to its file, moving the file when its name has changed.</summary>
    /// <param name="folder">The folder to keep it in, such as Orders/Returns; null leaves it where it is, or at the top when new.</param>
    public void Save(Workflow workflow, string? folder = null)
    {
        var previous = _workflows.TryGetValue(workflow.Id, out var known) ? known.Path : null;
        var directory = folder is not null ? FullFolder(folder)
            : previous is not null ? Path.GetDirectoryName(previous)!
            : Folder;
        Directory.CreateDirectory(directory);
        var path = previous is not null && Path.GetDirectoryName(previous) == directory && Path.GetFileName(previous) == FileName(workflow.Name)
            ? previous
            : FreePath(directory, workflow.Name);
        File.WriteAllText(path, JsonSerializer.Serialize(workflow, JsonFormats.Indented));
        if (previous is not null && previous != path && File.Exists(previous))
            File.Delete(previous);
        _workflows[workflow.Id] = (workflow, path);
        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        if (!_workflows.Remove(id, out var known))
            return;
        if (File.Exists(known.Path))
            File.Delete(known.Path);
        Changed?.Invoke();
    }

    public string? PathOf(string id) => _workflows.TryGetValue(id, out var known) ? known.Path : null;

    /// <summary>The folder a workflow is kept in, such as Orders/Returns; empty for the top.</summary>
    public string FolderOf(string id)
        => _workflows.TryGetValue(id, out var known) ? Relative(Path.GetDirectoryName(known.Path)!) : "";

    /// <summary>Every folder below the top, empty ones included, by path such as Orders/Returns.</summary>
    public IReadOnlyList<string> Folders
    {
        get
        {
            try
            {
                return Directory.Exists(Folder)
                    ? Directory.EnumerateDirectories(Folder, "*", SearchOption.AllDirectories).Where(d => !IsHidden(d)).Select(Relative)
                        .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList()
                    : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    /// <summary>Makes a folder, such as Orders/Returns, and gives its path.</summary>
    public string CreateFolder(string folder)
    {
        var parts = Parts(folder);
        if (parts.Count == 0)
            throw new ArgumentException("Name the folder.");
        Directory.CreateDirectory(FullFolder(folder));
        Changed?.Invoke();
        return string.Join('/', parts);
    }

    /// <summary>Deletes a folder that holds nothing; one with anything in it is left.</summary>
    public bool DeleteFolder(string folder)
    {
        var full = FullFolder(folder);
        if (full == Folder || !Directory.Exists(full) || Directory.EnumerateFileSystemEntries(full).Any())
            return false;
        Directory.Delete(full);
        Changed?.Invoke();
        return true;
    }

    // A folder path, levels made safe for file names, inside the top folder.
    private string FullFolder(string folder)
    {
        var full = Path.GetFullPath(Path.Combine([Folder, .. Parts(folder)]));
        return full.StartsWith(Folder, StringComparison.Ordinal) ? full : Folder;
    }

    private static List<string> Parts(string folder)
        => folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p is not "." and not "..")
            .Select(p => FileName(p)[..^Extension.Length])
            .ToList();

    private string Relative(string directory)
        => Path.GetRelativePath(Folder, directory) is var relative && relative != "." ? relative.Replace(Path.DirectorySeparatorChar, '/') : "";

    // Folders starting with a dot, such as .git, are not workflows' folders.
    private bool IsHidden(string path)
        => Path.GetRelativePath(Folder, path).Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.') && p is not ("." or ".."));

    /// <summary>A name not yet used by a workflow, such as "New workflow 2".</summary>
    public string FreeName(string name)
    {
        var candidate = name;
        for (var n = 2; _workflows.Values.Any(w => string.Equals(w.Workflow.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static string FreePath(string directory, string name)
    {
        var path = Path.Combine(directory, FileName(name));
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(directory, FileName($"{name} {n}"));
        return path;
    }

    private static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' ? '-' : c).ToArray());
        return (safe.Length == 0 ? "workflow" : safe) + Extension;
    }
}
