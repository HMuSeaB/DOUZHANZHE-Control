// SPDX-License-Identifier: MIT
//
// PawnIoDetection — PawnIO 驱动安装检测
// 启动时通过注册表和设备节点检测 PawnIO 是否已安装

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Douzhanzhe.HAL;

public static class PawnIoDetection
{
    /// <summary>PawnIO 驱动状态</summary>
    public enum DriverStatus
    {
        /// <summary>未安装</summary>
        NotInstalled,
        /// <summary>已安装但设备节点不可访问（需重启）</summary>
        InstalledNoDevice,
        /// <summary>已安装且设备节点可用</summary>
        Ready,
    }

    /// <summary>获取当前 PawnIO 驱动状态</summary>
    public static (DriverStatus Status, Version? Version, string? Detail) GetStatus()
    {
        // 1. 检查注册表
        var version = ReadInstalledVersion();
        if (version == null)
            return (DriverStatus.NotInstalled, null, "注册表未发现 PawnIO 安装记录");

        // 2. 尝试打开设备节点
        var deviceOk = ProbeDeviceNode();
        if (!deviceOk)
            return (DriverStatus.InstalledNoDevice, version, $"驱动 v{version} 已安装，但设备节点不可达（可能需要重启）");

        return (DriverStatus.Ready, version, $"驱动 v{version}，设备节点可达");
    }

    /// <summary>仅检查注册表中是否安装了 PawnIO</summary>
    public static Version? ReadInstalledVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");

            var raw = key?.GetValue("DisplayVersion");
            if (raw is string s && !string.IsNullOrWhiteSpace(s))
                return Version.TryParse(s, out var v) ? v : null;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    /// <summary>尝试打开设备节点验证可达性</summary>
    private static bool ProbeDeviceNode()
    {
        try
        {
            // 尝试 CreateFile 打开 PawnIO 设备（仅探测，不加载模块）
            var raw = NativeMethods.CreateFile(
                @"\\.\PawnIO",
                0x80000000 | 0x40000000, // GENERIC_READ | GENERIC_WRITE
                0x00000003,              // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3,                       // OPEN_EXISTING
                0,
                IntPtr.Zero);

            if (raw == IntPtr.Zero || raw.ToInt64() == -1)
                return false;

            NativeMethods.CloseHandle(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>返回友好描述文本</summary>
    public static string GetStatusText(DriverStatus status)
    {
        return status switch
        {
            DriverStatus.NotInstalled => "PawnIO 驱动未安装",
            DriverStatus.InstalledNoDevice => "PawnIO 驱动已安装，设备节点不可用（需重启）",
            DriverStatus.Ready => "PawnIO 驱动就绪",
            _ => "未知状态",
        };
    }

    /// <summary>启动时调用，记录日志</summary>
    public static void LogStatus()
    {
        var (status, version, detail) = GetStatus();
        var ver = version != null ? $"v{version}" : "N/A";
        AppLog.Write("PawnIO", $"[Detection] status={status}, version={ver}, detail={detail}");
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);
    }
}
