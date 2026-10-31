using System;
using Joktan.Dashboard.Telemetry;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

public class MachineSnapshotTests
{
    private static MachineSnapshot Snapshot(long? used, long? total)
        => new(null, used, total, used, total, "/", TimeSpan.Zero, 0);

    [Theory]
    [InlineData(25L, 100L, 0.25)]
    [InlineData(0L, 100L, 0.0)]
    [InlineData(100L, 100L, 1.0)]
    [InlineData(null, 100L, null)]
    [InlineData(25L, null, null)]
    [InlineData(25L, 0L, null)]
    [InlineData(null, null, null)]
    public void Works_out_how_much_is_in_use(long? used, long? total, double? expected)
    {
        var snapshot = Snapshot(used, total);

        Assert.Equal(expected, snapshot.MemoryUtilization);
        Assert.Equal(expected, snapshot.DiskUtilization);
    }
}
