using System.Threading;
using System.Threading.Tasks;
using Joktan.Dashboard.Telemetry;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

public class MachineMonitorTests
{
    [Fact]
    public void Has_a_reading_from_the_start()
    {
        using var monitor = new MachineMonitor();

        var latest = monitor.Latest;

        Assert.NotEmpty(latest.DiskMountPoint);
        Assert.True(latest.ProcessMemoryBytes > 0);
        Assert.True(latest.Uptime > System.TimeSpan.Zero);
    }

    [Fact]
    public void Readings_are_sensible_where_they_can_be_read()
    {
        using var monitor = new MachineMonitor();

        var latest = monitor.Latest;

        if (latest.MemoryUtilization is { } memory)
            Assert.InRange(memory, 0, 1);
        if (latest.DiskUtilization is { } disk)
            Assert.InRange(disk, 0, 1);
        if (latest.CpuUtilization is { } cpu)
            Assert.InRange(cpu, 0, 1);
    }

    [Fact]
    public async Task Takes_a_new_reading_on_its_timer()
    {
        using var monitor = new MachineMonitor();
        var updated = new TaskCompletionSource<MachineSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Updated += snapshot => updated.TrySetResult(snapshot);

        var snapshot = await updated.Task.WaitAsync(MachineMonitor.Interval * 5, TestContext.Current.CancellationToken);

        Assert.Same(snapshot, monitor.Latest);
    }
}
