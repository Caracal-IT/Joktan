using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Dashboard.Telemetry;

namespace Joktan.Dashboard.ViewModels;

public sealed record InfoRow(string Label, string Value);

public partial class DashboardViewModel : PageViewModel
{
    private const string Missing = "–";

    private readonly MetricTileViewModel _cpu = new("CPU usage", Icons.Cpu, hasMeter: true, hasTrend: true);
    private readonly MetricTileViewModel _memory = new("Memory used", Icons.Memory, hasMeter: true, hasTrend: true);
    private readonly MetricTileViewModel _disk = new("Disk used", Icons.Disk, hasMeter: true);
    private readonly MetricTileViewModel _uptime = new("Uptime", Icons.Clock);

    public DashboardViewModel(MachineMonitor monitor)
        : base("Overview", "Live metrics for this machine")
    {
        Tiles = [_cpu, _memory, _disk, _uptime];
        SystemInfo =
        [
            new InfoRow("Machine name", Environment.MachineName),
            new InfoRow("Operating system", RuntimeInformation.OSDescription),
            new InfoRow("Architecture", RuntimeInformation.OSArchitecture.ToString()),
            new InfoRow("Logical processors", Environment.ProcessorCount.ToString()),
            new InfoRow(".NET runtime", Environment.Version.ToString()),
        ];

        Apply(monitor.Latest);
        monitor.Updated += snapshot => Dispatcher.UIThread.Post(() => Apply(snapshot));
    }

    public IReadOnlyList<MetricTileViewModel> Tiles { get; }

    public IReadOnlyList<InfoRow> SystemInfo { get; }

    public string TelemetrySummary
        => $"Exported over OTLP to {TelemetryHost.Endpoint} from the {MachineMetrics.MeterName} meter.";

    /// <summary>The instruments exported through OpenTelemetry, with their latest values.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InfoRow> ExportedMetrics { get; set; } = [];

    private void Apply(MachineSnapshot s)
    {
        _cpu.Update(
            FormatPercent(s.CpuUtilization),
            $"{Environment.ProcessorCount} logical processors",
            s.CpuUtilization);
        _memory.Update(
            FormatPercent(s.MemoryUtilization),
            FormatUsage(s.MemoryUsedBytes, s.MemoryTotalBytes),
            s.MemoryUtilization);
        _disk.Update(
            FormatPercent(s.DiskUtilization),
            FormatUsage(s.DiskUsedBytes, s.DiskTotalBytes),
            s.DiskUtilization);
        _uptime.Update(FormatDuration(s.Uptime), $"Started {DateTime.Now - s.Uptime:g}");

        ExportedMetrics =
        [
            new InfoRow("system.cpu.utilization", FormatPercent(s.CpuUtilization)),
            new InfoRow("system.cpu.logical.count", Environment.ProcessorCount.ToString()),
            new InfoRow("system.memory.usage", FormatBytes(s.MemoryUsedBytes)),
            new InfoRow("system.memory.limit", FormatBytes(s.MemoryTotalBytes)),
            new InfoRow("system.memory.utilization", FormatPercent(s.MemoryUtilization)),
            new InfoRow("system.filesystem.usage", FormatBytes(s.DiskUsedBytes)),
            new InfoRow("system.filesystem.limit", FormatBytes(s.DiskTotalBytes)),
            new InfoRow("system.filesystem.utilization", FormatPercent(s.DiskUtilization)),
            new InfoRow("system.uptime", $"{s.Uptime.TotalSeconds:N0} s"),
            new InfoRow("process.memory.usage", FormatBytes(s.ProcessMemoryBytes)),
        ];
    }

    private static string FormatPercent(double? ratio)
        => ratio is { } r ? $"{r * 100:0}%" : Missing;

    private static string FormatUsage(long? used, long? total)
        => used is null || total is null ? "Not available" : $"{FormatBytes(used)} of {FormatBytes(total)}";

    private static string FormatBytes(long? bytes)
    {
        if (bytes is not { } b)
            return Missing;

        const double mb = 1024 * 1024;
        const double gb = mb * 1024;
        return b >= gb ? $"{b / gb:0.0} GB" : $"{b / mb:0} MB";
    }

    private static string FormatDuration(TimeSpan t)
        => t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h"
            : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m"
            : $"{t.Minutes}m";
}
