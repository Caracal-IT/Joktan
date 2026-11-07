using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Core;
using Joktan.Workflows.Model;
using Microsoft.Extensions.Logging;

namespace Joktan.Workflows.Engine;

/// <summary>Runs kept on disk, a file each: long ones as they go, so that they can carry on after a restart, and all of them when they finish.</summary>
public sealed class RunStore(string folder)
{
    private readonly object _serialGate = new();

    public string Folder { get; } = folder;

    // The last serial number given, beside the runs; not a run itself, as runs are the .json files.
    private string SerialPath => Path.Combine(Folder, "serial.txt");

    /// <summary>The next serial number, written down at once so that it is never given twice, even across a restart.</summary>
    /// <param name="atLeast">A number the next must be above, such as the highest of the runs read.</param>
    public long NextSerial(long atLeast = 0)
    {
        lock (_serialGate)
        {
            long last = 0;
            try
            {
                if (File.Exists(SerialPath))
                    long.TryParse(File.ReadAllText(SerialPath).Trim(), out last);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Unreadable, it starts again from the highest run there is.
            }

            var next = Math.Max(last, atLeast) + 1;
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(SerialPath, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppLog.For("Workflows").LogWarning("The serial number could not be saved: {Error}", e.Message);
            }

            return next;
        }
    }

    public void Save(WorkflowRun run)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = PathOf(run.Id);
            var temporary = path + ".tmp";

            // Written beside the file and then moved over it, so that a run is never left half written.
            File.WriteAllText(temporary, JsonSerializer.Serialize(run, JsonFormats.Compact));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.For("Workflows").LogWarning("Run {Run} could not be saved: {Error}", run.Id, e.Message);
        }
    }

    public List<WorkflowRun> LoadAll()
    {
        var runs = new List<WorkflowRun>();
        try
        {
            if (!Directory.Exists(Folder))
                return runs;
            foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<WorkflowRun>(File.ReadAllText(path)) is { Id.Length: > 0 } run)
                        runs.Add(run);
                }
                catch (JsonException)
                {
                    // A damaged run is left out.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.For("Workflows").LogWarning("Runs could not be read: {Error}", e.Message);
        }

        return runs.OrderBy(r => r.Started).ToList();
    }

    public void Delete(string id)
    {
        try
        {
            File.Delete(PathOf(id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone already, or left for next time.
        }
    }

    private string PathOf(string id) => Path.Combine(Folder, id + ".json");
}
