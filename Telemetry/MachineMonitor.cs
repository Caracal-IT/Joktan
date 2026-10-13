using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Joktan.Telemetry;

/// <summary>
/// Samples the local machine on a timer. The dashboard and the OpenTelemetry instruments both
/// read from here, so CPU usage is measured over one shared interval.
/// </summary>
public sealed class MachineMonitor : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly SystemReader _reader = SystemReader.Create();
    private readonly string _diskRoot;
    private readonly Timer _timer;
    private (ulong Idle, ulong Total)? _lastCpu;
    private volatile MachineSnapshot _latest;

    public MachineMonitor()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _diskRoot = Path.GetPathRoot(home) is { Length: > 0 } root ? root : "/";

        _lastCpu = Try(_reader.ReadCpuTimes);
        _latest = Sample();
        _timer = new Timer(_ => Tick(), null, Interval, Interval);
    }

    public MachineSnapshot Latest => _latest;

    /// <summary>Raised on a thread-pool thread after each sample.</summary>
    public event Action<MachineSnapshot>? Updated;

    public void Dispose() => _timer.Dispose();

    private void Tick()
    {
        var snapshot = Sample();
        _latest = snapshot;
        Updated?.Invoke(snapshot);
    }

    private MachineSnapshot Sample()
    {
        double? cpu = null;
        var times = Try(_reader.ReadCpuTimes);
        if (times is { } now && _lastCpu is { } last && now.Total > last.Total)
        {
            var total = now.Total - last.Total;
            var idle = now.Idle >= last.Idle ? now.Idle - last.Idle : 0;
            cpu = Math.Clamp(1.0 - (double)idle / total, 0, 1);
        }

        if (times is not null)
            _lastCpu = times;

        var memory = Try(_reader.ReadMemory);
        var disk = Try(ReadDisk);

        using var process = Process.GetCurrentProcess();
        return new MachineSnapshot(
            cpu,
            memory?.Used,
            memory?.Total,
            disk?.Used,
            disk?.Total,
            _diskRoot,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            process.WorkingSet64);
    }

    private (long Used, long Total)? ReadDisk()
    {
        var drive = new DriveInfo(_diskRoot);
        return (drive.TotalSize - drive.AvailableFreeSpace, drive.TotalSize);
    }

    // A counter that cannot be read is reported as missing rather than stopping the sampler.
    private static T? Try<T>(Func<T?> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
