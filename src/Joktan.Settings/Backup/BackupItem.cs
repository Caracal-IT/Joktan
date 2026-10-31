using System.Collections.Generic;

namespace Joktan.Settings.Backup;

/// <summary>A folder or file that goes into a backup.</summary>
/// <param name="Key">What it is called in the backup, so it can be found again whatever its path is by then.</param>
/// <param name="Exclude">Files and folders inside it that belong to something else, or to this computer.</param>
public sealed record BackupRoot(string Key, string Path, bool IsFile = false, IReadOnlyCollection<string>? Exclude = null);

/// <summary>One thing that can be ticked for a backup, such as the notes.</summary>
public sealed record BackupItem(string Id, string Name, IReadOnlyList<BackupRoot> Roots);
