using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Joktan.Telemetry;

/// <summary>
/// Reads machine-wide CPU and memory counters, which .NET does not expose in a portable way.
/// </summary>
internal abstract class SystemReader
{
    public static SystemReader Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsReader();
        if (OperatingSystem.IsLinux())
            return new LinuxReader();
        if (OperatingSystem.IsMacOS())
            return new MacReader();
        return new UnsupportedReader();
    }

    /// <summary>Cumulative CPU time since boot, summed over all cores, in arbitrary ticks.</summary>
    public abstract (ulong Idle, ulong Total)? ReadCpuTimes();

    public abstract (long Used, long Total)? ReadMemory();

    private sealed class UnsupportedReader : SystemReader
    {
        public override (ulong Idle, ulong Total)? ReadCpuTimes() => null;

        public override (long Used, long Total)? ReadMemory() => null;
    }

    private sealed class WindowsReader : SystemReader
    {
        public override (ulong Idle, ulong Total)? ReadCpuTimes()
        {
            // Kernel time includes idle time.
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return null;
            return (idle, kernel + user);
        }

        public override (long Used, long Total)? ReadMemory()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
                return null;
            return ((long)(status.TotalPhys - status.AvailPhys), (long)status.TotalPhys);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }

    private sealed class LinuxReader : SystemReader
    {
        public override (ulong Idle, ulong Total)? ReadCpuTimes()
        {
            // "cpu  user nice system idle iowait irq softirq steal guest guest_nice"
            using var reader = new StreamReader("/proc/stat");
            var fields = reader.ReadLine()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields is null || fields.Length < 5 || fields[0] != "cpu")
                return null;

            // Guest time is already counted in user and nice, so stop at steal.
            ulong total = 0;
            for (var i = 1; i < Math.Min(fields.Length, 9); i++)
                total += ulong.Parse(fields[i], CultureInfo.InvariantCulture);

            var idle = ulong.Parse(fields[4], CultureInfo.InvariantCulture);
            if (fields.Length > 5)
                idle += ulong.Parse(fields[5], CultureInfo.InvariantCulture);
            return (idle, total);
        }

        public override (long Used, long Total)? ReadMemory()
        {
            long? total = null, available = null;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    total = ParseKilobytes(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    available = ParseKilobytes(line);

                if (total is not null && available is not null)
                    return (total.Value - available.Value, total.Value);
            }

            return null;
        }

        // "MemTotal:       16303428 kB"
        private static long ParseKilobytes(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return long.Parse(parts[1], CultureInfo.InvariantCulture) * 1024;
        }
    }

    private sealed class MacReader : SystemReader
    {
        private const int HostCpuLoadInfo = 3;
        private const int HostVmInfo64 = 4;

        // vm_statistics64 is 152 bytes; it is read as 32-bit words and indexed below.
        private const int VmInfo64Words = 38;
        private const int VmPurgeableCount = 22;
        private const int VmWireCount = 3;
        private const int VmCompressorPageCount = 32;
        private const int VmInternalPageCount = 35;

        private readonly uint _host = mach_host_self();

        public override (ulong Idle, ulong Total)? ReadCpuTimes()
        {
            // Ticks are ordered user, system, idle, nice.
            var ticks = new uint[4];
            var count = (uint)ticks.Length;
            if (host_statistics(_host, HostCpuLoadInfo, ticks, ref count) != 0)
                return null;
            return (ticks[2], (ulong)ticks[0] + ticks[1] + ticks[2] + ticks[3]);
        }

        public override (long Used, long Total)? ReadMemory()
        {
            nuint size = sizeof(ulong);
            if (sysctlbyname("hw.memsize", out var total, ref size, IntPtr.Zero, 0) != 0)
                return null;

            var info = new uint[VmInfo64Words];
            var count = (uint)info.Length;
            if (host_statistics64(_host, HostVmInfo64, info, ref count) != 0)
                return null;

            // App memory + wired + compressed, the same sum Activity Monitor shows as "Memory Used".
            long pages = (long)info[VmInternalPageCount] - info[VmPurgeableCount]
                         + info[VmWireCount] + info[VmCompressorPageCount];
            return (pages * Environment.SystemPageSize, (long)total);
        }

        [DllImport("libSystem.dylib")]
        private static extern uint mach_host_self();

        [DllImport("libSystem.dylib")]
        private static extern int host_statistics(uint host, int flavor, [Out] uint[] info, ref uint count);

        [DllImport("libSystem.dylib")]
        private static extern int host_statistics64(uint host, int flavor, [Out] uint[] info, ref uint count);

        [DllImport("libSystem.dylib")]
        private static extern int sysctlbyname(string name, out ulong value, ref nuint size, IntPtr newValue, nuint newLength);
    }
}
