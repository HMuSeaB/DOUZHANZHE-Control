// SPDX-License-Identifier: MIT
//
// HardwareDetector — 两维硬件探测
// CPU 维度: 识别 AMD/Intel 厂商 + 型号家族 → 决定 SMU/MSR 控制路径
// OEM 维度: 识别主板厂商 → 决定 EC 寄存器映射 + WMI 方法表

using System.Management;

namespace Douzhanzhe.HAL;

/// <summary>AMD CPU 家族枚举（参考 UXTU Family.cs）</summary>
public enum RyzenFamily
{
    Unknown,
    DragonRange,    // Zen 4, 8940HX
}

/// <summary>Intel CPU 家族枚举</summary>
public enum IntelFamily
{
    Unknown,
    ArrowLakeHx,    // Core Ultra 7 251HX
}

/// <summary>OEM/品牌枚举</summary>
public enum OemVendor
{
    Unknown,
    Bellator,       // 斗战者 / 战系列 (Bellator N176)
}

/// <summary>硬件探测结果</summary>
public record PlatformInfo
{
    /// <summary>CPU 厂商: "AMD" 或 "Intel"</summary>
    public string Vendor { get; init; } = "";

    /// <summary>CPU 型号 (如 "Ryzen 9 8940HX")</summary>
    public string Model { get; init; } = "";

    /// <summary>是否为 AMD</summary>
    public bool IsAmd => Vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为 Intel</summary>
    public bool IsIntel => Vendor.Equals("Intel", StringComparison.OrdinalIgnoreCase);

    /// <summary>OEM 商标</summary>
    public OemVendor Oem { get; init; } = OemVendor.Unknown;

    /// <summary>主板型号 (如 "N176")</summary>
    public string OemBoard { get; init; } = "";

    /// <summary>当前平台的能力集</summary>
    public PlatformCapabilities Capabilities { get; init; } = null!;
}

public sealed class HardwareDetector
{
    private PlatformInfo? _cached;

    /// <summary>执行硬件探测（结果缓存，启动时只跑一次）</summary>
    public PlatformInfo Detect()
    {
        if (_cached != null) return _cached;

        var vendor = "";
        var model = "";
        var oem = OemVendor.Unknown;
        var oemBoard = "";

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Name FROM Win32_Processor");
            using var processors = searcher.Get();
            foreach (ManagementObject obj in processors)
            {
                using (obj)
                {
                    var mfr = obj["Manufacturer"]?.ToString() ?? "";
                    var name = obj["Name"]?.ToString() ?? "";
                    if (mfr.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                        vendor = "AMD";
                    else if (mfr.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                        vendor = "Intel";
                    model = name;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("HAL", $"HardwareDetector: WMI Win32_Processor failed: {ex.Message}");
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Product FROM Win32_BaseBoard");
            using var boards = searcher.Get();
            foreach (ManagementObject obj in boards)
            {
                using (obj)
                {
                    var mfr = obj["Manufacturer"]?.ToString() ?? "";
                    var product = obj["Product"]?.ToString() ?? "";
                    // OEM 检测: 品牌名可能出现在 Manufacturer 或 Product 字段
                    if (mfr.Contains("Bellator", StringComparison.OrdinalIgnoreCase) ||
                        product.Contains("Bellator", StringComparison.OrdinalIgnoreCase))
                    {
                        oem = OemVendor.Bellator;
                    }
                    oemBoard = product;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("HAL", $"HardwareDetector: WMI Win32_BaseBoard failed: {ex.Message}");
        }

        var caps = vendor switch
        {
            "AMD" => PlatformCapabilities.AmdFull,
            "Intel" => PlatformCapabilities.IntelLimited,
            _ => new PlatformCapabilities(),
        };

        _cached = new PlatformInfo
        {
            Vendor = vendor,
            Model = model,
            Oem = oem,
            OemBoard = oemBoard,
            Capabilities = caps,
        };

        AppLog.Write("HAL", $"HardwareDetector: vendor={vendor}, model={model}, oem={oem}, oemBoard={oemBoard}");
        return _cached;
    }
}
