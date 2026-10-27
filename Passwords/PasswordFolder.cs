using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Joktan.Passwords;

/// <summary>A folder of password entries, with the folders below it. The root folder has no name.</summary>
public sealed class PasswordFolder
{
    public string Id { get; set; } = NewId();

    public string Name { get; set; } = "";

    /// <summary>Whether the folders and entries below it are shown in the tree.</summary>
    public bool IsExpanded { get; set; } = true;

    public List<PasswordFolder> Folders { get; set; } = [];

    public List<PasswordEntry> Entries { get; set; } = [];

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Every entry in this folder and the folders below it.</summary>
    public IEnumerable<PasswordEntry> AllEntries() => Entries.Concat(Folders.SelectMany(f => f.AllEntries()));

    /// <summary>A copy of this folder and everything below it, with the same IDs.</summary>
    public PasswordFolder Copy() => new()
    {
        Id = Id,
        Name = Name,
        IsExpanded = IsExpanded,
        Folders = Folders.Select(f => f.Copy()).ToList(),
        Entries = Entries.Select(e => e.Copy()).ToList(),
    };
}

/// <summary>One saved login.</summary>
public sealed class PasswordEntry
{
    public string Id { get; set; } = PasswordFolder.NewId();

    public string Title { get; set; } = "";

    public string Username { get; set; } = "";

    /// <summary>
    /// The password, in a vault. In the system keychain the password is kept there instead, and this
    /// is null in the entry list.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; set; }

    public string Url { get; set; } = "";

    public string Notes { get; set; } = "";

    public DateTime Created { get; set; } = DateTime.UtcNow;

    public DateTime Modified { get; set; } = DateTime.UtcNow;

    public PasswordEntry Copy() => (PasswordEntry)MemberwiseClone();
}
