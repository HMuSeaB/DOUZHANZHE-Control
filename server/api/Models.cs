using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

// 端点的请求/响应模型与性能覆盖项持久化模型。
// 这些类型原本堆在 Program.cs 顶层语句之后，与启动逻辑混在一个文件里。

public record WmiCmdRequest(int Method, int? Value);
public record GpuSetRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("action")] string Action,
    [property: System.Text.Json.Serialization.JsonPropertyName("min")] int? Min,
    [property: System.Text.Json.Serialization.JsonPropertyName("max")] int? Max,
    [property: System.Text.Json.Serialization.JsonPropertyName("value")] int? Value
);
record ControlRequest(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("value")] int Value
);
record OsdShowRequest([property: JsonPropertyName("text")] string Text);
public record SmuRawRequest(uint Cmd, uint Arg0);
record SmuSetRequest(string Parameter, int ValueM);
public record FanSetRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("largeRpm")] int? LargeRpm,
    [property: System.Text.Json.Serialization.JsonPropertyName("smallRpm")] int? SmallRpm
);
public record FanTestWriteRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("strategy")] string? Strategy,
    [property: System.Text.Json.Serialization.JsonPropertyName("largeRpm")] int? LargeRpm,
    [property: System.Text.Json.Serialization.JsonPropertyName("smallRpm")] int? SmallRpm
);
// ---- NVAPI 请求模型 ----
public record NvapiOverclockRequest(
    [property: JsonPropertyName("coreOffsetMhz")] int CoreOffsetMhz,
    [property: JsonPropertyName("memOffsetMhz")] int MemOffsetMhz
);
public record NvapiPowerLimitRequest(
    [property: JsonPropertyName("powerW")] int PowerW
);
public record NvapiThermalLimitRequest(
    [property: JsonPropertyName("tempC")] float TempC
);
// ---- CPU 性能控制请求模型 ----
public record CpuFreqLimitRequest(
    [property: JsonPropertyName("mhz")] int Mhz  // 0 = 取消限制
);
public record CpuTurboRequest(
    [property: JsonPropertyName("enabled")] bool Enabled
);
public record CpuCoreLimitRequest(
    [property: JsonPropertyName("percent")] int Percent  // 0-100
);
// ---- Node.js 迁移端点请求/响应模型 ----
public record UxtuApplyRequest(
    UxtuParams? Params,
    UxtuLimits? Limits
);
public record UxtuParams(
    [property: JsonPropertyName("cpuLongPptW")] int? CpuLongPptW,
    [property: JsonPropertyName("cpuShortPptW")] int? CpuShortPptW,
    [property: JsonPropertyName("cpuTempLimitC")] int? CpuTempLimitC,
    [property: JsonPropertyName("gpuPptLimitW")] int? GpuPptLimitW,
    [property: JsonPropertyName("cpuVoltageOffset")] int? CpuVoltageOffset,
    [property: JsonPropertyName("cpuFreqLimitEnabled")] bool? CpuFreqLimitEnabled,
    [property: JsonPropertyName("cpuFreqLimitMhz")] int? CpuFreqLimitMhz,
    [property: JsonPropertyName("cpuTurboDisabled")] bool? CpuTurboDisabled,
    [property: JsonPropertyName("cpuCoreLimit")] int? CpuCoreLimit
);
public record UxtuLimits(
    [property: JsonPropertyName("cpu")] UxtuCpuLimits? Cpu,
    [property: JsonPropertyName("gpu")] UxtuGpuLimits? Gpu
);
public record UxtuCpuLimits(
    [property: JsonPropertyName("pptLimitW")] int? PptLimitW,
    [property: JsonPropertyName("tempLimitC")] int? TempLimitC
);
public record UxtuGpuLimits(
    [property: JsonPropertyName("pptLimitW")] int? PptLimitW
);
public record SystemSettingsRequest(string? Key, int? Value);
public record UiState(string[]? CardOrder, string[]? HiddenCards)
{
    public UiState() : this(null, null) { }
    public string[] CardOrder { get; init; } = CardOrder ?? Array.Empty<string>();
    public string[] HiddenCards { get; init; } = HiddenCards ?? Array.Empty<string>();
}
public record DefaultConfig(string[]? Order, string[]? Hidden)
{
    public DefaultConfig() : this(null, null) { }
    public string[] Order { get; init; } = Order ?? Array.Empty<string>();
    public string[] Hidden { get; init; } = Hidden ?? Array.Empty<string>();
}

// ---- Fan Curve 请求模型 ----
public record FanCurveSaveRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("points")] List<FanCurvePointDto>? Points,
    [property: System.Text.Json.Serialization.JsonPropertyName("intervalMs")] int? IntervalMs,
    [property: System.Text.Json.Serialization.JsonPropertyName("hysteresisC")] int? HysteresisC
);
public record FanCurvePointDto(
    [property: System.Text.Json.Serialization.JsonPropertyName("temp")] int Temp,
    [property: System.Text.Json.Serialization.JsonPropertyName("largeRpm")] int LargeRpm,
    [property: System.Text.Json.Serialization.JsonPropertyName("smallRpm")] int SmallRpm
);
public record FanCurveStartRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("intervalMs")] int? IntervalMs,
    [property: System.Text.Json.Serialization.JsonPropertyName("hysteresisC")] int? HysteresisC
);

// ---- 性能设置持久化模型 ----
public class CpuOverrides { public int? FreqLimitMhz; public bool? TurboEnabled; public int? CoreLimitPercent; }
public class GpuOverrides { public int? CoreFreqMhz; public bool? FreqLocked; public int? MemFreqLevel; }
public class NvapiOverrides { public int? OcCoreOffsetMhz; public int? OcMemOffsetMhz; public int? PowerLimitW; public float? ThermalLimitC; }
public class SmuOverrides { public int? StapmLimitW; public int? ShortPowerLimitW; public int? TempLimitC; public int? CoAll; }
public class FanOverrides { public int? LargeRpm; public int? SmallRpm; }
public class PerformanceOverrides { public CpuOverrides Cpu = new(); public GpuOverrides Gpu = new(); public NvapiOverrides Nvapi = new(); public SmuOverrides Smu = new(); public FanOverrides Fan = new(); public int? PowerPlan; }

// ---- 快捷键请求模型 ----
public record HotkeyConfigRequest(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("modifiers")] string? Modifiers,
    [property: JsonPropertyName("key")] string? Key
);

// ---- 系统级 P/Invoke ----
public static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
