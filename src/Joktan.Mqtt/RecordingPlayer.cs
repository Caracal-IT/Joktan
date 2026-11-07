using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Joktan.Mqtt;

/// <summary>
/// Sends the messages of a recording again, keeping the gaps between them, scaled by <see cref="Speed"/>.
/// The speed can be changed, and playing paused, while it plays.
/// </summary>
public sealed class RecordingPlayer
{
    /// <summary>The speeds offered, from slowest to fastest, as multiples of the speed it was recorded at.</summary>
    public static IReadOnlyList<double> Speeds { get; } = [0.1, 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100];

    // The longest it sleeps at a time, so that a change of speed or a pause takes effect at once.
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MinLoop = TimeSpan.FromMilliseconds(100);

    private readonly IReadOnlyList<RecordedMessage> _messages;
    private readonly TimeSpan _length;
    private readonly Func<RecordedMessage, Task<bool>> _send;
    private readonly Lock _lock = new();
    private double _speed = 1;
    private bool _paused;
    private TimeSpan _position;
    private int _next;
    private int _sent;
    private int _failed;

    /// <param name="send">Sends one message; returns whether it was sent.</param>
    public RecordingPlayer(Recording recording, Func<RecordedMessage, Task<bool>> send)
    {
        _messages = recording.Messages;
        // A loop takes some time even when everything arrived at once, so it cannot flood the broker.
        _length = recording.Length < MinLoop ? MinLoop : recording.Length;
        _send = send;
    }

    public double Speed
    {
        get { lock (_lock) return _speed; }
        set { lock (_lock) _speed = Math.Clamp(value, Speeds[0], Speeds[^1]); }
    }

    public bool IsPaused
    {
        get { lock (_lock) return _paused; }
        set { lock (_lock) _paused = value; }
    }

    /// <summary>Whether it starts again from the beginning after the end.</summary>
    public bool Loop { get; set; }

    /// <summary>How far into the recording it is, in the recording's own time.</summary>
    public TimeSpan Position
    {
        get { lock (_lock) return _position; }
    }

    /// <summary>The messages sent since it started, over every loop.</summary>
    public int Sent => Volatile.Read(ref _sent);

    public int Failed => Volatile.Read(ref _failed);

    /// <summary>How many messages of this pass through the recording are behind it.</summary>
    public int Done => Volatile.Read(ref _next);

    /// <summary>Plays the recording to its end, or until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var now = clock.Elapsed;
                TimeSpan position;
                double speed;
                lock (_lock)
                {
                    if (!_paused)
                        _position += (now - last) * _speed;
                    position = _position;
                    speed = _speed;
                }

                last = now;

                while (_next < _messages.Count && _messages[_next].At <= position && !cancel.IsCancellationRequested && !IsPaused)
                {
                    if (await _send(_messages[_next]))
                        Interlocked.Increment(ref _sent);
                    else
                        Interlocked.Increment(ref _failed);
                    Interlocked.Increment(ref _next);
                }

                if (_next >= _messages.Count)
                {
                    // A loop waits out the quiet time at the end, so the gap from last to first is kept.
                    if (!Loop || _messages.Count == 0)
                    {
                        lock (_lock)
                            _position = _length;
                        return;
                    }

                    if (position >= _length)
                    {
                        lock (_lock)
                            _position = TimeSpan.Zero;
                        Volatile.Write(ref _next, 0);
                        continue;
                    }
                }

                var until = _next < _messages.Count ? _messages[_next].At : _length;
                var wait = IsPaused ? Slice : (until - position) / speed;
                await Task.Delay(wait < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : wait > Slice ? Slice : wait, cancel);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }
}
