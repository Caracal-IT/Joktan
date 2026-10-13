using System;

namespace Joktan.Telemetry;

/// <summary>
/// One reading of the local machine. A null value means it could not be read on this platform.
/// </summary>
public sealed record MachineSnapshot(
    double? CpuUtilization,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    long? DiskUsedBytes,
    long? DiskTotalBytes,
    string DiskMountPoint,
    TimeSpan Uptime,
    long ProcessMemoryBytes)
{
    public double? MemoryUtilization => Ratio(MemoryUsedBytes, MemoryTotalBytes);

    public double? DiskUtilization => Ratio(DiskUsedBytes, DiskTotalBytes);

    private static double? Ratio(long? used, long? total)
        => used is { } u && total is > 0 ? (double)u / total.Value : null;
}
