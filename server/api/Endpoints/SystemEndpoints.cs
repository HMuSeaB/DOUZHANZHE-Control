using System.Diagnostics;
using System.Text.Json;
using Douzhanzhe.HAL;

namespace Douzhanzhe.API.Endpoints;

/// <summary>
/// 遥测快照、机器配置信息、键鼠与散热模式等系统开关，以及日志导出。
/// </summary>
public static class SystemEndpoints
{
    static void Log(string msg) => AppLog.Write("API", msg);

    // 扩展信息要拉起 PowerShell 子进程，开销大，缓存 60 秒
    static string _sysInfoExtCache = "";
    static DateTime _sysInfoExtTime = DateTime.MinValue;

    public static void MapSystemEndpoints(this WebApplication app, string logDir)
    {
        MapTelemetryEndpoints(app);
        MapControlEndpoints(app);
        MapHotkeyEndpoints(app);
        MapDiagnosticsEndpoints(app, logDir);
    }

    static void MapTelemetryEndpoints(WebApplication app)
    {
        app.MapGet("/api/telemetry", (HardwareAbstractionLayer hal, WmiInterface wmi) =>
        {
            return Results.Json(new
            {
                cpuUsage = hal.CpuUsage,
                cpuTemp = hal.CpuTemperature,
                cpuFreq = hal.CpuFreq,
                cpuCores = hal.CpuCores,
                gpuUsage = hal.GpuUsage,
                gpuTemp = hal.GpuTemperature,
                gpuFreq = hal.GpuFreq,
                gpuVram = hal.GpuVram,
                gpuVramUsed = hal.GpuVramUsed,
                gpuMemMhz = hal.GpuMemMhz,
                gpuPowerDrawW = hal.GpuPowerDrawW,
                fanLargeRpm = hal.CpuFanRpm,
                fanSmallRpm = hal.GpuFanRpm,
                fanLargeMax = HardwareAbstractionLayer.FanLargeMax,
                fanSmallMax = HardwareAbstractionLayer.FanSmallMax,
                memoryUsage = hal.MemoryUsage,
                memoryTotalGB = hal.MemoryTotalGB,
                memoryFreq = hal.MemoryFreq,
                diskUsage = hal.DiskUsage,
                diskTotalGB = hal.DiskTotalGB,
                diskFreeGB = hal.DiskFreeGB,
                kbBrightness = hal.KeyboardBrightness,
                fnLock = wmi.Available ? wmi.GetFnLock() == 1 : hal.FnLock,
                numLock = hal.NumLock,
                capsLock = hal.CapsLock,
                thermalMode = wmi.Available ? wmi.GetThermalMode() : hal.ThermalMode,
                powerPlan = hal.PowerPlan,
                touchpadLock = wmi.Available ? wmi.GetTouchpadLock() == 1 : hal.TouchpadLocked,
                igpuOnly = hal.IgpuOnly,
                gpuMode = wmi.Available ? wmi.GetGpuMode().ToString() : null,
            });
        });

        app.MapGet("/api/system/info", (HardwareAbstractionLayer hal) =>
        {
            return Results.Json(new
            {
                systemModel = hal.SystemModel,
                cpuName = hal.CpuName,
                cpuCores = hal.CpuCores,
                cpuFreq = Math.Round((double)hal.CpuFreq, 1),
                gpuDiscrete = hal.GpuDiscreteName,
                gpuIntegrated = hal.GpuIntegratedName,
                memoryTotalGB = hal.MemoryTotalGB,
                memoryFreq = hal.MemoryFreq,
                diskTotalGB = hal.DiskTotalGB,
            });
        });

        // Extended system info (BIOS/OS/disks/memory sticks/GPU driver) — single PowerShell call
        app.MapGet("/api/system/info-ext", () =>
        {
            if ((DateTime.UtcNow - _sysInfoExtTime).TotalSeconds < 60 && !string.IsNullOrEmpty(_sysInfoExtCache))
                return Results.Content(_sysInfoExtCache, "application/json; charset=utf-8");
            try
            {
                // bin/<config>/net8.0/ → project root (up 3), or bin/run/ or bin/build/ → project root (up 2)
                var scriptPath = Path.Combine(AppContext.BaseDirectory, "sysinfo-ext.ps1");
                if (!File.Exists(scriptPath))
                    scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "sysinfo-ext.ps1");
                if (!File.Exists(scriptPath))
                    scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "sysinfo-ext.ps1");
                if (!File.Exists(scriptPath))
                    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "sysinfo-ext.ps1");
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                if (!p.WaitForExit(8000)) { p.Kill(); return Results.Json(new { error = "timeout" }); }
                var json = p.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(json))
                {
                    _sysInfoExtCache = json;
                    _sysInfoExtTime = DateTime.UtcNow;
                }
                return Results.Content(_sysInfoExtCache, "application/json; charset=utf-8");
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/system/info-ext");
            }
        });

        app.MapGet("/api/health", (HardwareAbstractionLayer hal) =>
        {
            return Results.Json(new
            {
                ok = hal.HealthCheck(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        });

        app.MapGet("/api/discover", (HardwareAbstractionLayer hal) =>
        {
            return Results.Json(new
            {
                available = hal.HealthCheck(),
                ecBase = $"0x{DriverBridge.EC_BASE:X}",
                driverLoaded = DriverBridge.Instance.Ready,
                touchpad = true,
            });
        });
    }

    static void MapControlEndpoints(WebApplication app)
    {
        app.MapPost("/api/control", (
            ControlRequest req,
            HardwareAbstractionLayer hal,
            WmiInterface wmi,
            ConfigStore config,
            OsdService osd,
            ProcessMonitorService processMonitor) =>
        {
            try
            {
                Log($"[control] ← target={req.Target} value={req.Value}");
                switch (req.Target)
                {
                    case "kb_light":
                        hal.KeyboardBrightness = (byte)int.Clamp(req.Value, 0, 3);
                        break;
                    case "fn_lock":
                        wmi.SetFnLock(req.Value != 0);
                        break;
                    case "num_lock":
                        hal.NumLock = req.Value != 0;
                        break;
                    case "caps_lock":
                        hal.CapsLock = req.Value != 0;
                        break;
                    case "touchpad_lock":
                        wmi.SetTouchpadLock(req.Value != 0);
                        break;
                    case "power_plan":
                        hal.PowerPlan = req.Value;
                        config.SavePerfOverrides(o => o.PowerPlan = req.Value);
                        break;
                    case "thermal_mode":
                        // 优先走 WMI Method 8 (SystemPerMode) — 固件完整加载模式预设
                        // WMI 不可用时降级到 EC 直写
                        var clampedMode = (byte)int.Clamp(req.Value, 0, 3);
                        Log($"[control/thermal_mode] ← mode={clampedMode} (raw={req.Value})");
                        if (wmi.Available)
                            wmi.SetThermalMode(clampedMode);
                        else
                            hal.ThermalMode = clampedMode;
                        // OSD 提示（模式切换时自动触发）
                        var modeNames = new[] { "office", "beast", "silent", "gaming" };
                        if (clampedMode < modeNames.Length)
                        {
                            osd.Show(modeNames[clampedMode]);
                            // 通知 ProcessMonitorService 更新当前模式
                            processMonitor.UpdateCurrentMode(modeNames[clampedMode]);
                        }
                        break;
                    case "igpu_only":
                        hal.IgpuOnly = req.Value != 0;
                        break;
                    case "ec_write":
                        break;
                    case "gpu_mode":
                        {
                            var gpuVal = (byte)int.Clamp(req.Value, 0, 2);
                            if (!wmi.SetGpuMode(gpuVal))
                                return Results.Problem("WMI GPUMode failed", statusCode: 500);
                            // 持久化用户选择的 GPU 模式，重启后自动恢复
                            config.Write("gpu-mode.json", new { gpuMode = gpuVal });
                        }
                        break;
                    case string t when t.StartsWith("ec_write:"):
                        {
                            var parts_ = t.Split(':');
                            if (parts_.Length >= 2 && parts_[1].StartsWith("0x"))
                            {
                                byte reg = Convert.ToByte(parts_[1], 16);
                                hal.WriteEcPort(reg, (byte)req.Value);
                            }
                        }
                        break;
                    default:
                        return Results.Problem($"未知控制目标: {req.Target}", statusCode: 400);
                }
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/control");
            }
        });

        // ---- OSD API ----
        app.MapPost("/api/osd/show", (OsdShowRequest req, OsdService osd) =>
        {
            if (!string.IsNullOrWhiteSpace(req.Text))
                osd.Show(req.Text);
            return Results.Ok();
        });

        // ---- 关闭显示器 ----
        app.MapPost("/api/monitor/off", () =>
        {
            NativeMethods.SendMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));
            return Results.Ok(new { ok = true });
        });
    }

    // ---- 快捷键配置 ----
    static void MapHotkeyEndpoints(WebApplication app)
    {
        app.MapGet("/api/hotkey/monitor-off", (ConfigStore config) =>
        {
            var cfgPath = Path.Combine(config.ConfigDir, "hotkey-config.json");
            if (!File.Exists(cfgPath))
                return Results.Json(new { enabled = true, modifiers = "ctrl,shift", key = "Q", conflict = false });
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
                var root = doc.RootElement;
                var mo = root.TryGetProperty("monitorOff", out var moVal) ? moVal : root;
                bool conflict = false;
                var statusPath = Path.Combine(config.ConfigDir, "hotkey-status.json");
                if (File.Exists(statusPath))
                {
                    try
                    {
                        using var sDoc = JsonDocument.Parse(File.ReadAllText(statusPath));
                        conflict = sDoc.RootElement.TryGetProperty("monitorOffConflict", out var cv) && cv.GetBoolean();
                    }
                    catch { }
                }
                return Results.Json(new
                {
                    enabled = mo.TryGetProperty("enabled", out var ev) ? ev.GetBoolean() : true,
                    modifiers = mo.TryGetProperty("modifiers", out var mv) ? mv.GetString() : "ctrl,shift",
                    key = mo.TryGetProperty("key", out var kv) ? kv.GetString() : "Q",
                    conflict
                });
            }
            catch
            {
                return Results.Json(new { enabled = true, modifiers = "ctrl,shift", key = "Q", conflict = false });
            }
        });

        app.MapPost("/api/hotkey/monitor-off", (HotkeyConfigRequest req, ConfigStore config) =>
        {
            var cfgPath = Path.Combine(config.ConfigDir, "hotkey-config.json");
            // 读取现有配置并合并
            var existing = new Dictionary<string, object>();
            if (File.Exists(cfgPath))
            {
                try { existing = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(cfgPath)) ?? new(); } catch { }
            }
            var monitorOff = new Dictionary<string, object>
            {
                ["enabled"] = req.Enabled,
                ["modifiers"] = req.Modifiers ?? "ctrl,shift",
                ["key"] = req.Key ?? "Q"
            };
            existing["monitorOff"] = monitorOff;
            config.Write("hotkey-config.json", existing);
            return Results.Ok(new { ok = true });
        });
    }

    static void MapDiagnosticsEndpoints(WebApplication app, string logDir)
    {
        // ---- 日志导出（供用户反馈问题时使用）----
        app.MapGet("/api/logs/export", () =>
        {
            var logFile = Path.Combine(logDir, "app.log");
            if (!File.Exists(logFile))
                return Results.Json(new { ok = false, error = "日志文件不存在" });
            var bytes = File.ReadAllBytes(logFile);
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Results.File(bytes, "text/plain; charset=utf-8", $"douzhanzhe-log-{ts}.log");
        });

        // ---- Node.js 后端遗留端点，保留仅为兼容旧前端 ----
        app.MapPost("/api/system/settings", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = JsonSerializer.Deserialize<SystemSettingsRequest>(await reader.ReadToEndAsync());
                Log($"[system] {body?.Key}={body?.Value} — Node.js 已废弃，此端点仅做兼容");
                return Results.Json(new { ok = false, error = "此端点已废弃，请使用 /api/control" });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/system/settings"); }
        });

        app.MapPost("/api/fan/full-speed", () =>
        {
            return Results.Json(new { ok = false, error = "此端点已废弃，请使用 /api/fan/set-target 手动控制风扇" });
        });
    }
}
