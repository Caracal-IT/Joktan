using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Joktan.Dashboard.Telemetry;
using Joktan.Dashboard.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Dashboard.Tests.ViewModels;

public class DashboardViewModelTests
{
    private const long Gb = 1024L * 1024 * 1024;

    // Readings come from the machine, so what they turn into is checked with chosen ones.
    private static void Apply(DashboardViewModel vm, MachineSnapshot snapshot) =>
        typeof(DashboardViewModel).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [snapshot]);

    private static string Exported(DashboardViewModel vm, string name) => vm.ExportedMetrics.Single(r => r.Label == name).Value;

    [Fact]
    public Task Starts_with_the_latest_reading_and_this_machine() => Headless.Run(() =>
    {
        using var monitor = new MachineMonitor();

        var vm = new DashboardViewModel(monitor);

        Assert.Equal(["CPU usage", "Memory used", "Disk used", "Uptime"], vm.Tiles.Select(t => t.Label));
        Assert.Contains(vm.SystemInfo, r => r.Label == "Machine name" && r.Value == Environment.MachineName);
        Assert.Contains(TelemetryHost.Endpoint, vm.TelemetrySummary);
        Assert.Contains(MachineMetrics.MeterName, vm.TelemetrySummary);
        Assert.Equal(10, vm.ExportedMetrics.Count);
    });

    [Fact]
    public Task Shows_readings_as_percentages_and_sizes() => Headless.Run(() =>
    {
        using var monitor = new MachineMonitor();
        var vm = new DashboardViewModel(monitor);

        Apply(vm, new MachineSnapshot(0.5, 512 * 1024 * 1024, 2 * Gb, 3 * Gb, 4 * Gb, "/", TimeSpan.FromMinutes(5), 10 * 1024 * 1024));

        Assert.Equal("50%", vm.Tiles[0].Value);
        Assert.Equal("25%", vm.Tiles[1].Value);
        Assert.Equal($"512 MB of {2.0:0.0} GB", vm.Tiles[1].Detail);
        Assert.Equal("75%", vm.Tiles[2].Value);
        Assert.Equal($"{3.0:0.0} GB of {4.0:0.0} GB", vm.Tiles[2].Detail);
        Assert.Equal("5m", vm.Tiles[3].Value);
        Assert.Equal("10 MB", Exported(vm, "process.memory.usage"));
        Assert.Equal("300 s", Exported(vm, "system.uptime"));
    });

    [Fact]
    public Task A_reading_that_is_missing_says_so() => Headless.Run(() =>
    {
        using var monitor = new MachineMonitor();
        var vm = new DashboardViewModel(monitor);

        Apply(vm, new MachineSnapshot(null, null, null, 1, null, "/", TimeSpan.Zero, 0));

        Assert.Equal("–", vm.Tiles[0].Value);
        Assert.Equal("Not available", vm.Tiles[1].Detail);
        Assert.Equal("Not available", vm.Tiles[2].Detail);
        Assert.Equal("–", Exported(vm, "system.memory.usage"));
    });

    [Theory]
    [InlineData(3 * 24 * 60 + 125, "3d 2h")]
    [InlineData(125, "2h 5m")]
    [InlineData(59, "59m")]
    public Task Uptime_is_shown_in_the_largest_units(int minutes, string expected) => Headless.Run(() =>
    {
        using var monitor = new MachineMonitor();
        var vm = new DashboardViewModel(monitor);

        Apply(vm, monitor.Latest with { Uptime = TimeSpan.FromMinutes(minutes) });

        Assert.Equal(expected, vm.Tiles[3].Value);
    });

    [Fact]
    public Task New_readings_from_the_monitor_arrive_on_their_own() => Headless.Run(async () =>
    {
        using var monitor = new MachineMonitor();
        var vm = new DashboardViewModel(monitor);
        var before = vm.ExportedMetrics;

        await Headless.Until(() => !ReferenceEquals(before, vm.ExportedMetrics), timeoutMs: 15000);
    });
}
