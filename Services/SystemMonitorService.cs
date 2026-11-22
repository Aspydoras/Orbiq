using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Orbiq.Services;

public sealed class SystemMonitorService
{
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _hasCpuSample;

    public SystemMetrics Read()
    {
        double cpu = ReadCpuUsage();
        ReadMemory(out double usedMemoryGb, out double totalMemoryGb, out double memoryPercent);
        ReadBattery(out int? batteryPercent, out bool pluggedIn);
        ReadSystemDrive(out double freeGb, out double totalGb);
        return new SystemMetrics(cpu, usedMemoryGb, totalMemoryGb, memoryPercent,
            batteryPercent, pluggedIn, freeGb, totalGb);
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
            return 0;

        ulong idleNow = idle.ToUInt64();
        ulong kernelNow = kernel.ToUInt64();
        ulong userNow = user.ToUInt64();
        if (!_hasCpuSample)
        {
            _lastIdle = idleNow;
            _lastKernel = kernelNow;
            _lastUser = userNow;
            _hasCpuSample = true;
            return 0;
        }

        ulong idleDelta = idleNow - _lastIdle;
        ulong totalDelta = (kernelNow - _lastKernel) + (userNow - _lastUser);
        _lastIdle = idleNow;
        _lastKernel = kernelNow;
        _lastUser = userNow;
        if (totalDelta == 0) return 0;
        return Math.Clamp(100.0 * (1.0 - (double)idleDelta / totalDelta), 0, 100);
    }

    private static void ReadMemory(out double usedGb, out double totalGb, out double percent)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            usedGb = totalGb = percent = 0;
            return;
        }
        totalGb = status.TotalPhysical / 1024d / 1024d / 1024d;
        usedGb = (status.TotalPhysical - status.AvailablePhysical) / 1024d / 1024d / 1024d;
        percent = Math.Clamp(100d * usedGb / totalGb, 0, 100);
    }

    private static void ReadBattery(out int? percentage, out bool pluggedIn)
    {
        percentage = null;
        pluggedIn = false;
        if (!GetSystemPowerStatus(out SystemPowerStatus status)) return;
        pluggedIn = status.AcLineStatus == 1;
        if (status.BatteryLifePercent != 255 && status.BatteryFlag != 128)
            percentage = Math.Clamp((int)status.BatteryLifePercent, 0, 100);
    }

    private static void ReadSystemDrive(out double freeGb, out double totalGb)
    {
        freeGb = totalGb = 0;
        try
        {
            string? root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return;
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;
            freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            totalGb = drive.TotalSize / 1024d / 1024d / 1024d;
        }
        catch { /* Некоторые сетевые/съёмные системные тома могут не отдавать сведения. */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

public sealed record SystemMetrics(
    double CpuPercent,
    double UsedMemoryGb,
    double TotalMemoryGb,
    double MemoryPercent,
    int? BatteryPercent,
    bool IsPluggedIn,
    double SystemDriveFreeGb,
    double SystemDriveTotalGb);
