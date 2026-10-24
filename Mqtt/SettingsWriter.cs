using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Joktan.Mqtt;

/// <summary>
/// Writes message settings files on a background thread, so a switch flipped in the list takes
/// effect at once and the disk catches up. When a file is written again before the earlier write
/// has happened, only the latest content is written.
/// </summary>
public sealed class SettingsWriter
{
    // A change the folder watcher reports this soon after a write is taken to be the write itself.
    private static readonly TimeSpan OwnChangeWindow = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _written = new(StringComparer.Ordinal);
    private readonly Action<string, Exception> _failed;
    private Task _worker = Task.CompletedTask;
    private bool _running;

    /// <param name="failed">Called on a background thread with the path of a file that could not be written.</param>
    public SettingsWriter(Action<string, Exception> failed)
    {
        _failed = failed;
    }

    /// <summary>Whether a write is waiting or in progress.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    public void Write(string path, string content)
    {
        path = Path.GetFullPath(path);
        lock (_gate)
        {
            _pending[path] = content;
            if (_running)
                return;

            _running = true;
            _worker = Task.Run(Drain);
        }
    }

    /// <summary>Completes once everything queued so far is on disk.</summary>
    public Task FlushAsync()
    {
        lock (_gate)
            return _worker;
    }

    /// <summary>Whether a file was just written here, so that a change reported for it is not news.</summary>
    public bool WroteRecently(string path)
        => _written.TryGetValue(Path.GetFullPath(path), out var at) && DateTime.UtcNow - at < OwnChangeWindow;

    private void Drain()
    {
        while (true)
        {
            KeyValuePair<string, string> next;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _running = false;
                    return;
                }

                next = _pending.First();
                _pending.Remove(next.Key);
            }

            _written[next.Key] = DateTime.UtcNow;
            try
            {
                File.WriteAllText(next.Key, next.Value);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _failed(next.Key, e);
            }

            _written[next.Key] = DateTime.UtcNow;
        }
    }
}
