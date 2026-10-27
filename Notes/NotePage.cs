using System;
using System.Collections.Generic;

namespace Joktan.Notes;

/// <summary>A page of notes in the index, with the pages below it. Its text is kept in a file of its own.</summary>
public sealed class NotePage
{
    public string Id { get; set; } = NewId();

    public string Title { get; set; } = "";

    /// <summary>Whether the pages below it are shown in the tree.</summary>
    public bool IsExpanded { get; set; } = true;

    public List<NotePage> Children { get; set; } = [];

    public static string NewId() => Guid.NewGuid().ToString("N");
}
