using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Joktan.Logs;

/// <summary>A line of a source file, numbered from 1.</summary>
public sealed record SourceLine(int Number, string Text);

/// <summary>A line that matched a search, with the file it is in, relative to the folder.</summary>
public sealed record SourceMatch(string File, int Line, string Text);

/// <summary>
/// An app's source code, which stack traces are followed into and the agent may read. Nothing outside the folder is
/// ever read, whatever path is asked for, links included.
/// </summary>
public sealed class SourceFolder
{
    public const int MaxLinesRead = 400;
    private const long MaxFileSize = 2 * 1024 * 1024;
    private const int MaxLineLength = 300;

    // Folders of build output, packages and tools, which hold no code worth searching.
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults", "packages", "__pycache__", ".venv",
    };

    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public SourceFolder(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root { get; }

    public bool Exists => Directory.Exists(Root);

    /// <summary>
    /// The file in the folder that a path from a stack trace names. The path is where the app was built, so the
    /// longest end of it that is in the folder is taken: /build/src/App/A.cs is src/App/A.cs here.
    /// </summary>
    public string? Find(string path)
    {
        if (!Path.IsPathRooted(path) && Resolve(path) is { } relative && File.Exists(relative))
            return relative;

        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "." or ".." || parts[i].EndsWith(':'))
                continue;
            var candidate = Path.Combine([Root, .. parts[i..]]);
            if (File.Exists(candidate) && IsInside(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    /// <summary>A path in the folder, relative to it or whole, or null when it would be outside.</summary>
    public string? Resolve(string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
        return IsInside(full) ? full : null;
    }

    public string Relative(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');

    /// <summary>Whether a whole path is in the folder, following any links on the way to it.</summary>
    public bool IsInside(string full)
    {
        if (!Under(Path.GetFullPath(full)))
            return false;

        for (var path = Path.GetFullPath(full); path.Length > Root.Length; path = Path.GetDirectoryName(path)!)
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target && !Under(target.FullName))
                return false;
        }

        return true;
    }

    private bool Under(string full)
        => full.Length > Root.Length && full.StartsWith(Root, PathComparison) && full[Root.Length] is '/' or '\\';

    /// <summary>Lines of a file in the folder, from one line for so many, and how many lines it has.</summary>
    public (IReadOnlyList<SourceLine> Lines, int Total) Read(string file, int from, int count)
    {
        var full = Resolve(file) ?? throw new UnauthorizedAccessException("The file is outside the source folder.");
        from = Math.Max(1, from);
        count = Math.Clamp(count, 1, MaxLinesRead);
        var lines = new List<SourceLine>();
        var total = 0;
        foreach (var line in File.ReadLines(full))
        {
            total++;
            if (total >= from && total < from + count)
                lines.Add(new SourceLine(total, line));
        }

        return (lines, total);
    }

    /// <summary>The lines that hold a text, ignoring case, in files matching a pattern such as <c>*.cs</c>.</summary>
    public IReadOnlyList<SourceMatch> Search(string text, string? pattern, int limit, CancellationToken cancel = default)
    {
        var matches = new List<SourceMatch>();
        if (text.Length == 0)
            return matches;

        foreach (var file in Files(Root, string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim()))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(file).Length > MaxFileSize || IsBinary(file) || !IsInside(file))
                    continue;
                var number = 0;
                foreach (var line in File.ReadLines(file))
                {
                    number++;
                    if (!line.Contains(text, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var shown = line.Trim();
                    matches.Add(new SourceMatch(Relative(file), number, shown.Length > MaxLineLength ? shown[..MaxLineLength] + "…" : shown));
                    if (matches.Count >= limit)
                        return matches;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be read is passed over.
            }
        }

        return matches;
    }

    private static IEnumerable<string> Files(string folder, string pattern)
    {
        IEnumerable<string> files, folders;
        try
        {
            files = Directory.EnumerateFiles(folder, pattern, new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive }).Order(StringComparer.Ordinal).ToList();
            folders = Directory.EnumerateDirectories(folder, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
            yield return file;
        foreach (var inner in folders.Where(f => !Skipped.Contains(Path.GetFileName(f))))
        {
            foreach (var file in Files(inner, pattern))
                yield return file;
        }
    }

    private static bool IsBinary(string file)
    {
        var buffer = new byte[8000];
        using var stream = File.OpenRead(file);
        var read = stream.Read(buffer, 0, buffer.Length);
        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    /// <summary>The lines of text around one, for showing a frame of a stack trace.</summary>
    public static string Around(IReadOnlyList<SourceLine> lines, int line)
    {
        var text = new StringBuilder();
        var width = lines.Count == 0 ? 1 : lines[^1].Number.ToString().Length;
        foreach (var source in lines)
            text.Append(source.Number == line ? "→ " : "  ").Append(source.Number.ToString().PadLeft(width)).Append("  ").AppendLine(source.Text);
        return text.ToString().TrimEnd();
    }
}
