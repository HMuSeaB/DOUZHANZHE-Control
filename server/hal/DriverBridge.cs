// SPDX-License-Identifier: MIT
//
// DriverBridge — 硬件桥接层（v2.0 PawnIO 路径，已移除 inpoutx64）

using System.Threading;

namespace Douzhanzhe.HAL;

public sealed class DriverBridge : IDisposable
{
    public const uint EC_BASE = 0xFE800400;

    static readonly Lazy<DriverBridge> _instance = new(() => new DriverBridge(), LazyThreadSafetyMode.ExecutionAndPublication);
    readonly object _ecLock = new();
    volatile bool _dis;

    PawnIoDevice? _pawnIo;
    bool _usePawnIo;

    DriverBridge() { }
    public static DriverBridge Instance => _instance.Value;
    public bool Ready => _usePawnIo;

    public void Init()
    {
        if (_usePawnIo || _dis) return;
        InitPawnIO();
    }

    public void Dispose()
    {
        _dis = true;
        _pawnIo?.Dispose();
        _pawnIo = null;
    }

    public byte ReadEc(byte reg)
    {
        if (!_usePawnIo || _pawnIo == null) return 0;
        lock (_ecLock)
        {
            try
            {
                _pawnIo.Execute("ioctl_pio_write", [0x66, 0x80], 0);
                Thread.Sleep(2);
                _pawnIo.Execute("ioctl_pio_write", [0x62, reg], 0);
                Thread.Sleep(5);
                var result = _pawnIo.Execute("ioctl_pio_read", [0x62], 1);
                return result.Length > 0 ? (byte)result[0] : (byte)0;
            }
            catch { return 0; }
        }
    }

    public void WriteEc(byte reg, byte val)
    {
        if (!_usePawnIo || _pawnIo == null) return;
        lock (_ecLock)
        {
            try
            {
                _pawnIo.Execute("ioctl_pio_write", [0x66, 0x81], 0);
                Thread.Sleep(5);
                _pawnIo.Execute("ioctl_pio_write", [0x62, reg], 0);
                Thread.Sleep(5);
                _pawnIo.Execute("ioctl_pio_write", [0x62, val], 0);
                Thread.Sleep(10);
            }
            catch { }
        }
    }

    public bool InitPawnIO(string? binDir = null)
    {
        try
        {
            binDir ??= LocatePawnIOBinDir();
            if (binDir == null)
            {
                AppLog.Write("DriverBridge", "InitPawnIO: 未找到 PawnIO .bin 目录");
                return false;
            }
            var binPath = System.IO.Path.Combine(binDir, "LpcACPIEC.bin");
            if (!System.IO.File.Exists(binPath))
            {
                AppLog.Write("DriverBridge", $"InitPawnIO: LpcACPIEC.bin 不存在 ({binPath})");
                return false;
            }
            var detection = PawnIoDetection.GetStatus();
            if (detection.Status != PawnIoDetection.DriverStatus.Ready)
            {
                AppLog.Write("DriverBridge", $"InitPawnIO: {detection.Detail}");
                return false;
            }
            var device = PawnIoDevice.LoadModuleFromFile(binPath);
            if (!device.IsLoaded)
            {
                AppLog.Write("DriverBridge", "InitPawnIO: 加载 LpcACPIEC.bin 失败");
                device.Dispose();
                return false;
            }
            var test = device.Execute("ioctl_pio_read", [0x66], 1);
            if (test.Length == 0)
            {
                AppLog.Write("DriverBridge", "InitPawnIO: EC 测试读失败");
                device.Dispose();
                return false;
            }
            _pawnIo = device;
            _usePawnIo = true;
            AppLog.Write("DriverBridge", "InitPawnIO: PawnIO LpcACPIEC 就绪");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("DriverBridge", $"InitPawnIO 异常: {ex.Message}");
            _pawnIo?.Dispose();
            _pawnIo = null;
            _usePawnIo = false;
            return false;
        }
    }

    static string? LocatePawnIOBinDir()
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "PawnIO"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", "PawnIO"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "PawnIO"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "assets", "PawnIO"),
        };
        foreach (var c in candidates)
        {
            var full = System.IO.Path.GetFullPath(c);
            if (System.IO.Directory.Exists(full))
                return full;
        }
        return null;
    }
}
