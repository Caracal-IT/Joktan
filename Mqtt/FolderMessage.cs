using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Joktan.Mqtt;

public enum FolderMessageState
{
    Idle,
    Scheduled,
    Finished,
    Missed,
    Disabled,
    Invalid,
}

/// <summary>
/// A JSON file found in the message folder, with its settings and its progress in the current run.
/// </summary>
public sealed class FolderMessage
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>The folder path relative to the chosen folder; empty for a file with no topic folder.</summary>
    public required string Topic { get; init; }

    /// <summary>The file content exactly as written.</summary>
    public string Text { get; init; } = "";

    public JsonNode? Body { get; init; }

    public MessageSettings Settings { get; init; } = MessageSettings.Default;

    /// <summary>Why the message cannot be sent, or null.</summary>
    public string? Error { get; init; }

    public bool IsValid => Error is null;

    public FolderMessageState State { get; set; }

    public DateTime? NextDue { get; set; }

    public DateTime? LastSent { get; set; }

    /// <summary>Sends in the current run, by hand or by schedule.</summary>
    public int SentCount { get; set; }

    /// <summary>Scheduled sends in the current run, counted against the schedule's maximum.</summary>
    public int ScheduledSends { get; set; }

    public Dictionary<string, long> Counters { get; private set; } = [];

    public FolderMessageState RestingState
        => !IsValid ? FolderMessageState.Invalid
            : !Settings.Enabled ? FolderMessageState.Disabled
            : FolderMessageState.Idle;

    public void CarryOver(FolderMessage previous)
    {
        LastSent = previous.LastSent;
        SentCount = previous.SentCount;
        Counters = previous.Counters;
    }
}
