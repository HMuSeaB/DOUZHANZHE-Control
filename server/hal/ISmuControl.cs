// SPDX-License-Identifier: MIT
//
// ISmuControl — SMU/功耗控制抽象接口
// 两平台共同实现: AmdSmuController (AMD SMU) / IntelPowerController (Intel MSR)

namespace Douzhanzhe.HAL;

public interface ISmuControl
{
    /// <summary>设置长时功耗限制 (STAPM/PL1)，单位 mW</summary>
    int SetPowerLimit(uint mW);

    /// <summary>设置温度墙，单位 °C</summary>
    int SetTempLimit(uint celsius);

    /// <summary>设置短时功耗限制 (PPT fast/slow)，单位 mW</summary>
    int SetShortPowerLimit(uint fastMw, uint slowMw);

    /// <summary>全核 Curve Optimizer 偏移，单位 mV，负数=降压</summary>
    int SetCurveOptimizer(int mV);

    /// <summary>关闭睿频</summary>
    int SetTurboDisabled(bool disabled);

    /// <summary>批量应用（单次调用完成多项设置，效率更高）</summary>
    int BatchApply(uint? stapmMw, uint? fastMw, uint? slowMw,
                   uint? tempC, int? coAllMv, bool? turboOff);

    /// <summary>逐核 Curve Optimizer（AMD 实现，Intel 返回不支持）</summary>
    int SetPerCoreCO(int coreId, int offset);

    /// <summary>探测 SMU/MSR 是否可达</summary>
    bool Probe();

    /// <summary>返回当前平台的能力集</summary>
    PlatformCapabilities GetCapabilities();
}

/// <summary>平台能力集，前端据此决定渲染哪些控件</summary>
public record PlatformCapabilities
{
    public bool PowerLimit { get; init; }
    public bool TempLimit { get; init; }
    public bool ShortPowerLimit { get; init; }
    public bool CurveOptimizer { get; init; }
    public bool PerCoreCO { get; init; }
    public bool TurboDisabled { get; init; }
    public bool CoreLimit { get; init; }

    /// <summary>AMD 平台完整能力</summary>
    public static PlatformCapabilities AmdFull => new()
    {
        PowerLimit = true,
        TempLimit = true,
        ShortPowerLimit = true,
        CurveOptimizer = true,
        PerCoreCO = true,
        TurboDisabled = true,
        CoreLimit = true,
    };

    /// <summary>Intel 平台精简能力（受 MSR 写白名单限制）</summary>
    public static PlatformCapabilities IntelLimited => new()
    {
        PowerLimit = true,
        TempLimit = false,
        ShortPowerLimit = true,
        CurveOptimizer = false,
        PerCoreCO = false,
        TurboDisabled = false,
        CoreLimit = true,
    };
}
