using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.IO;

namespace FloatMate.Services;

public sealed class SystemMonitor
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private long _previousReceived;
    private long _previousSent;
    private DateTime _previousNetworkAt = DateTime.Now;

    public SystemSnapshot Read()
    {
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var disk = ReadDisk();
        var network = ReadNetwork();
        return new SystemSnapshot(cpu, memory.Percent, memory.UsedGb, memory.TotalGb,
            disk.Percent, disk.FreeGb, network.DownKb, network.UpKb);
    }

    private double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);
        if (_previousKernel == 0)
        {
            _previousIdle = idleValue;
            _previousKernel = kernelValue;
            _previousUser = userValue;
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var totalDelta = (kernelValue - _previousKernel) + (userValue - _previousUser);
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;
        return totalDelta == 0 ? 0 : Math.Clamp((1d - idleDelta / (double)totalDelta) * 100d, 0, 100);
    }

    private static (double Percent, double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return (0, 0, 0);
        const double gb = 1024d * 1024 * 1024;
        var total = status.TotalPhysical / gb;
        var used = (status.TotalPhysical - status.AvailablePhysical) / gb;
        return (status.MemoryLoad, used, total);
    }

    private static (double Percent, double FreeGb) ReadDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            const double gb = 1024d * 1024 * 1024;
            var usedPercent = (1d - drive.AvailableFreeSpace / (double)drive.TotalSize) * 100d;
            return (usedPercent, drive.AvailableFreeSpace / gb);
        }
        catch { return (0, 0); }
    }

    private (double DownKb, double UpKb) ReadNetwork()
    {
        long received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var stats = adapter.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch { }
        }

        var now = DateTime.Now;
        var seconds = Math.Max((now - _previousNetworkAt).TotalSeconds, 0.1);
        var down = _previousReceived == 0 ? 0 : Math.Max(0, received - _previousReceived) / 1024d / seconds;
        var up = _previousSent == 0 ? 0 : Math.Max(0, sent - _previousSent) / 1024d / seconds;
        _previousReceived = received;
        _previousSent = sent;
        _previousNetworkAt = now;
        return (down, up);
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
