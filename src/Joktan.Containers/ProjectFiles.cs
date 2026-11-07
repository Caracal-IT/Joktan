using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Joktan.Containers;

/// <summary>
/// Writing the files of the compose projects Joktan makes itself, for the brokers and the databases.
/// Every file starts with <see cref="Marker"/>, so that only those are ever replaced or deleted.
/// </summary>
internal static class ProjectFiles
{
    public const string Marker = "# Written by Joktan";

    public static bool IsOurs(string path)
    {
        if (!File.Exists(path))
            return false;
        using var reader = new StreamReader(path);
        return reader.ReadLine()?.StartsWith(Marker, StringComparison.Ordinal) == true;
    }

    public static void DeleteIfOurs(string path)
    {
        if (IsOurs(path))
            File.Delete(path);
    }

    public static void DeleteIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }

    public static void WriteIfChanged(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content);
    }

    // Files holding a password, its hash or a key, and their folders, are for the user alone.
    public static void CreatePrivateFolder(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void WritePrivate(string path, string content, bool onlyIfChanged = false)
    {
        if (onlyIfChanged && File.Exists(path) && File.ReadAllText(path) == content)
            return;
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, content);
            return;
        }

        using (var stream = new FileStream(path, new FileStreamOptions
               {
                   Mode = FileMode.Create,
                   Access = FileAccess.Write,
                   UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
               }))
        {
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        // A file that was already there keeps its mode, so it is set again.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
