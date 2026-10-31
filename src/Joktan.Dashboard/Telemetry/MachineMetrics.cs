using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Joktan.Dashboard.Telemetry;

/// <summary>
/// Publishes <see cref="MachineMonitor"/> readings as OpenTelemetry instruments, named after the
/// OpenTelemetry system and process semantic conventions.
/// </summary>
public sealed class MachineMetrics : IDisposable
{
    public const string MeterName = "Joktan.Machine";

    private readonly Meter _meter = new(MeterName);

    public MachineMetrics(MachineMonitor monitor)
    {
        _meter.CreateObservableGauge("system.cpu.utilization",
            () => Observe(monitor.Latest.CpuUtilization),
            "1", "Share of CPU time in use across all cores");
        _meter.CreateObservableGauge("system.cpu.logical.count",
            () => Environment.ProcessorCount,
            "{cpu}", "Number of logical processors");

        _meter.CreateObservableGauge("system.memory.usage",
            () => Observe(monitor.Latest.MemoryUsedBytes),
            "By", "Physical memory in use");
        _meter.CreateObservableGauge("system.memory.limit",
            () => Observe(monitor.Latest.MemoryTotalBytes),
            "By", "Total physical memory");
        _meter.CreateObservableGauge("system.memory.utilization",
            () => Observe(monitor.Latest.MemoryUtilization),
            "1", "Share of physical memory in use");

        _meter.CreateObservableGauge("system.filesystem.usage",
            () => Observe(monitor.Latest.DiskUsedBytes, MountPoint(monitor)),
            "By", "Space in use on the system drive");
        _meter.CreateObservableGauge("system.filesystem.limit",
            () => Observe(monitor.Latest.DiskTotalBytes, MountPoint(monitor)),
            "By", "Capacity of the system drive");
        _meter.CreateObservableGauge("system.filesystem.utilization",
            () => Observe(monitor.Latest.DiskUtilization, MountPoint(monitor)),
            "1", "Share of the system drive in use");

        _meter.CreateObservableGauge("system.uptime",
            () => monitor.Latest.Uptime.TotalSeconds,
            "s", "Time since the machine started");
        _meter.CreateObservableGauge("process.memory.usage",
            () => monitor.Latest.ProcessMemoryBytes,
            "By", "Physical memory used by this application");
    }

    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> MountPoint(MachineMonitor monitor)
        => new("system.filesystem.mountpoint", monitor.Latest.DiskMountPoint);

    // An unreadable value reports no measurement instead of a misleading zero.
    private static IEnumerable<Measurement<T>> Observe<T>(T? value, params KeyValuePair<string, object?>[] tags)
        where T : struct
        => value is { } v ? [new Measurement<T>(v, tags)] : [];
}
