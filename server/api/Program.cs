using Douzhanzhe.HAL;
using Douzhanzhe.API;
using System.Net.WebSockets;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Douzhanzhe.API.Endpoints;

// ---- AppLog 统一日志初始化（所有服务注册之前）----
var _appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Douzhanzhe Console");
var _logDir = Path.Combine(_appDataDir, "logs");
AppLog.Init(_logDir);
LocalAccessGuard.InitToken(_appDataDir);

// 提升进程与主线程优先级，确保在游戏满载时遥测采样与风扇控制仍能及时响应
var proc = Process.GetCurrentProcess();
proc.PriorityClass = ProcessPriorityClass.High;
Thread.CurrentThread.Priority = ThreadPriority.Highest;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<HardwareAbstractionLayer>();
builder.Services.AddSingleton<SmuController>();
builder.Services.AddSingleton<GpuController>();
builder.Services.AddSingleton<NvapiGpuController>();
builder.Services.AddSingleton<CpuPowerController>();
builder.Services.AddSingleton<WmiInterface>();
builder.Services.AddSingleton<FanCurveService>();
builder.Services.AddSingleton<OsdService>();
builder.Services.AddSingleton<GameProfileService>();
builder.Services.AddSingleton<ProcessMonitorService>();
builder.Services.AddHostedService<TelemetryBackgroundService>();

// ---- Config directory ----
// 安装环境: AppContext.BaseDirectory\config\
// 开发环境: BaseDirectory\bin\build\ → 需要回退到项目根目录\config\
var configDir = Path.Combine(AppContext.BaseDirectory, "config");
if (!Directory.Exists(configDir))
{
    var devConfig = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "config"));
    if (Directory.Exists(devConfig))
        configDir = devConfig;
}
var configStore = new ConfigStore(configDir);
builder.Services.AddSingleton(configStore);

var app = builder.Build();
var osdService = app.Services.GetRequiredService<OsdService>();

// ---- 本机同源守卫 ----
// 前端与 API 同源（都由本 Kestrel 提供），因此不需要任何 CORS 放行；
// 而 /api 下存在裸硬件写入能力，必须拒绝一切跨站来源。
var _devMode = app.Environment.IsDevelopment();
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    if (path.StartsWithSegments("/api") || path.StartsWithSegments("/ws"))
    {
        if (!LocalAccessGuard.IsAllowed(ctx, _devMode, out var denyReason))
        {
            AppLog.Write("Guard", $"拒绝 {ctx.Request.Method} {path} — {denyReason}");
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求来源不被信任" });
            return;
        }
    }
    await next();
});
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // index.html 禁止缓存（确保更新后前端 JS bundle 立即生效）
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});
app.MapFallbackToFile("index.html");

// ---- File logger (统一走 AppLog) ----
void Log(string msg)
{
    AppLog.Write("API", msg);
}
Log($"API starting, BaseDir={AppContext.BaseDirectory}, ConfigDir={configDir}");
Log($"[Guard] 同源守卫已启用 (devMode={_devMode}, 裸硬件端点={(LocalAccessGuard.UnsafeToolsEnabled ? "已解锁" : "禁用")}), 会话令牌={LocalAccessGuard.TokenPath}");

// ---- 性能设置持久化（实现见 ConfigStore）----
bool _pgSuppress = false; // ParameterGuard 睡眠期间暂停标志
PerformanceOverrides LoadPerfOverrides() => configStore.LoadPerfOverrides();
void SavePerfOverrides(Action<PerformanceOverrides> mutate) => configStore.SavePerfOverrides(mutate);

// ---- 睡眠/休眠恢复：重置底层驱动并重新初始化 ----
SystemEvents.PowerModeChanged += (sender, e) =>
{
    if (e.Mode == PowerModes.Suspend)
    {
        // 系统即将进入睡眠，暂停 ParameterGuard 和 HealthWatchdog 恢复
        _pgSuppress = true;
        TelemetryBackgroundService.SetSleeping(true);
        Log("[PowerEvent] 系统即将睡眠，暂停 ParameterGuard + HealthWatchdog");
    }
    else if (e.Mode == PowerModes.Resume)
    {
        Log("[PowerEvent] 系统从睡眠恢复，重置底层驱动...");
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                // inpoutx64 内核驱动在 S3/S4 后可能失效，必须重置映射并重新初始化
                DriverBridge.Instance.RecoverAfterSleep();

                // LHM 需要重新初始化（SMN 总线可能在睡眠后失效）
                LhmSensor.Close();
                LhmSensor.Open();

                // NVAPI 也需要重新初始化（GPU 驱动可能在睡眠后重新加载）
                var nv = app.Services.GetRequiredService<NvapiGpuController>();
                nv.Init();

                // GPU 模式恢复 (从 gpu-mode.json)
                var saved = JsonRead<Dictionary<string, int>>("gpu-mode.json", new Dictionary<string, int>());
                if (saved.TryGetValue("gpuMode", out int mode) && mode >= 0 && mode <= 2)
                {
                    var wmi = app.Services.GetRequiredService<WmiInterface>();
                    if (wmi.Available)
                    {
                        if (wmi.SetGpuMode((byte)mode))
                            Log($"[PowerEvent] GPU mode → {mode}");
                        else
                            Log($"[PowerEvent] GPU mode restore to {mode} failed");
                    }
                }

                // 恢复所有性能设置 (CPU/SMU/GPU/NVAPI/固定风扇)
                await RestoreAllPerfSettings("PowerEvent");

                // 自定义风扇曲线: 重新下发 ITSM 模式 + 重置 ShouldWrite 状态
                var fanCurve = app.Services.GetRequiredService<FanCurveService>();
                fanCurve.RecoverAfterSleep();

                Log("[PowerEvent] 全部恢复完成");

                // 等 SMU 完全稳定后再恢复 ParameterGuard 和 HealthWatchdog
                await System.Threading.Tasks.Task.Delay(30_000);
                _pgSuppress = false;
                TelemetryBackgroundService.SetSleeping(false);
                Log("[PowerEvent] ParameterGuard + HealthWatchdog 已恢复");
            }
            catch (Exception ex)
            {
                Log($"[PowerEvent] 恢复异常: {ex.Message}");
            }
        });
    }
};
// ---- JSON persistence helpers（实现见 ConfigStore）----
T JsonRead<T>(string fileName, T fallback) where T : class => configStore.Read(fileName, fallback);
void JsonWrite<T>(string fileName, T data) => configStore.Write(fileName, data);

// ---- 恢复计算类性能设置 (CPU + SMU + GPU + NVAPI, 不含风扇) ----
// 供启动、睡眠恢复、ParameterGuard 共用
async System.Threading.Tasks.Task RestoreComputeSettings(string tag)
{
    try
    {
        var o = LoadPerfOverrides();
        int restored = 0;

        // --- CPU (powercfg) ---
        if (o.Cpu.FreqLimitMhz.HasValue)
        {
            try { await app.Services.GetRequiredService<CpuPowerController>().SetFreqLimitAsync(o.Cpu.FreqLimitMhz.Value); restored++; Log($"[{tag}] CPU freq limit → {o.Cpu.FreqLimitMhz.Value} MHz"); }
            catch (Exception ex) { Log($"[{tag}] CPU freq limit failed: {ex.Message}"); }
        }
        if (o.Cpu.TurboEnabled.HasValue)
        {
            try { await app.Services.GetRequiredService<CpuPowerController>().SetTurboAsync(o.Cpu.TurboEnabled.Value); restored++; Log($"[{tag}] CPU turbo → {o.Cpu.TurboEnabled.Value}"); }
            catch (Exception ex) { Log($"[{tag}] CPU turbo failed: {ex.Message}"); }
        }
        if (o.Cpu.CoreLimitPercent.HasValue && o.Cpu.CoreLimitPercent.Value > 0)
        {
            try { await app.Services.GetRequiredService<CpuPowerController>().SetCoreLimitAsync(o.Cpu.CoreLimitPercent.Value); restored++; Log($"[{tag}] CPU core limit → {o.Cpu.CoreLimitPercent.Value}%"); }
            catch (Exception ex) { Log($"[{tag}] CPU core limit failed: {ex.Message}"); }
        }

        // --- SMU (ryzenadj) — 合并为单次 BatchApply 调用，避免 4 次串行进程启动 ---
        var smu = app.Services.GetRequiredService<SmuController>();
        {
            var stapmMw = o.Smu.StapmLimitW.HasValue ? (uint?)(o.Smu.StapmLimitW.Value * 1000) : null;
            var fastMw = o.Smu.ShortPowerLimitW.HasValue ? (uint?)(o.Smu.ShortPowerLimitW.Value * 1000) : null;
            var slowMw = fastMw;
            var tempC = o.Smu.TempLimitC.HasValue ? (uint?)o.Smu.TempLimitC.Value : null;
            var coAll = o.Smu.CoAll.HasValue ? (int?)o.Smu.CoAll.Value : null;

            if (stapmMw.HasValue || fastMw.HasValue || tempC.HasValue || coAll.HasValue)
            {
                try
                {
                    smu.BatchApply(stapmMw, fastMw, slowMw, tempC, coAll, null);
                    int smuCount = 0;
                    if (stapmMw.HasValue) { smuCount++; Log($"[{tag}] SMU stapm → {o.Smu.StapmLimitW!.Value}W"); }
                    if (fastMw.HasValue) { smuCount++; Log($"[{tag}] SMU short power → {o.Smu.ShortPowerLimitW!.Value}W"); }
                    if (tempC.HasValue) { smuCount++; Log($"[{tag}] SMU temp → {o.Smu.TempLimitC!.Value}°C"); }
                    if (coAll.HasValue) { smuCount++; Log($"[{tag}] SMU CO → {o.Smu.CoAll!.Value}"); }
                    restored += smuCount;
                }
                catch (Exception ex) { Log($"[{tag}] SMU BatchApply failed: {ex.Message}"); }
            }
        }

        // --- GPU (nvidia-smi) ---
        var gpu = app.Services.GetRequiredService<GpuController>();
        if (o.Gpu.CoreFreqMhz.HasValue && o.Gpu.CoreFreqMhz.Value > 0)
        {
            try
            {
                gpu.SetMaxGpuClock(o.Gpu.CoreFreqMhz.Value);
                if (o.Gpu.FreqLocked == true) gpu.SetExactGpuClock(o.Gpu.CoreFreqMhz.Value);
                restored++;
                Log($"[{tag}] GPU core → {o.Gpu.CoreFreqMhz.Value} MHz (locked={o.Gpu.FreqLocked})");
            }
            catch (Exception ex) { Log($"[{tag}] GPU core failed: {ex.Message}"); }
        }
        if (o.Gpu.MemFreqLevel.HasValue && o.Gpu.MemFreqLevel.Value > 0)
        {
            try
            {
                var memMap = new int[] { 0, 9001, 11001, 12001 };
                var idx = Math.Clamp(o.Gpu.MemFreqLevel.Value, 0, 3);
                if (idx > 0) gpu.SetMaxMemoryClock(memMap[idx]);
                restored++;
                Log($"[{tag}] GPU mem level → {idx} ({memMap[idx]} MHz)");
            }
            catch (Exception ex) { Log($"[{tag}] GPU mem failed: {ex.Message}"); }
        }

        // --- NVAPI ---
        var nv = app.Services.GetRequiredService<NvapiGpuController>();
        if (o.Nvapi.OcCoreOffsetMhz.HasValue || o.Nvapi.OcMemOffsetMhz.HasValue)
        {
            try
            {
                nv.SetP0Offset(o.Nvapi.OcCoreOffsetMhz ?? 0, o.Nvapi.OcMemOffsetMhz ?? 0);
                restored++;
                Log($"[{tag}] NVAPI OC → core={o.Nvapi.OcCoreOffsetMhz ?? 0}, mem={o.Nvapi.OcMemOffsetMhz ?? 0}");
            }
            catch (Exception ex) { Log($"[{tag}] NVAPI OC failed: {ex.Message}"); }
        }
        if (o.Nvapi.PowerLimitW.HasValue)
        {
            try { nv.SetPowerLimit((uint)(o.Nvapi.PowerLimitW.Value * 1000)); restored++; Log($"[{tag}] NVAPI power → {o.Nvapi.PowerLimitW.Value}W"); }
            catch (Exception ex) { Log($"[{tag}] NVAPI power failed: {ex.Message}"); }
        }
        if (o.Nvapi.ThermalLimitC.HasValue)
        {
            try { nv.SetThermalLimit(o.Nvapi.ThermalLimitC.Value); restored++; Log($"[{tag}] NVAPI thermal → {o.Nvapi.ThermalLimitC.Value}°C"); }
            catch (Exception ex) { Log($"[{tag}] NVAPI thermal failed: {ex.Message}"); }
        }

        // --- 电源计划 ---
        if (o.PowerPlan.HasValue)
        {
            try
            {
                var hal2 = app.Services.GetRequiredService<HardwareAbstractionLayer>();
                hal2.PowerPlan = o.PowerPlan.Value;
                restored++;
                var planNames = new[] { "平衡", "高性能", "节能" };
                var idx = Math.Clamp(o.PowerPlan.Value, 0, 2);
                Log($"[{tag}] Power plan → {planNames[idx]} ({idx})");
            }
            catch (Exception ex) { Log($"[{tag}] Power plan failed: {ex.Message}"); }
        }

        if (restored > 0) Log($"[{tag}] Compute settings restored: {restored} applied");
        else Log($"[{tag}] No compute settings to restore");
    }
    catch (Exception ex) { Log($"[{tag}] Compute settings restore failed: {ex.Message}"); }
}

// ---- 恢复所有性能设置 (启动 + 睡眠恢复共用, 含风扇) ----
async System.Threading.Tasks.Task RestoreAllPerfSettings(string tag)
{
    await RestoreComputeSettings(tag);

    // --- 固定风扇转速 (仅启动和睡眠恢复时执行, ParameterGuard 不调用) ---
    try
    {
        var o = LoadPerfOverrides();
        if (o.Fan.LargeRpm.HasValue || o.Fan.SmallRpm.HasValue)
        {
            var wmi = app.Services.GetRequiredService<WmiInterface>();
            if (o.Fan.LargeRpm.HasValue)
            {
                var speed = (byte)Math.Clamp(o.Fan.LargeRpm.Value / 100, 0, 44);
                wmi.SetFanManual(0, true);
                wmi.SetFanSpeed(0, speed);
            }
            if (o.Fan.SmallRpm.HasValue)
            {
                var speed = (byte)Math.Clamp(o.Fan.SmallRpm.Value / 100, 0, 82);
                wmi.SetFanManual(1, true);
                wmi.SetFanSpeed(1, speed);
            }
            Log($"[{tag}] Fan target → large={o.Fan.LargeRpm ?? 0} small={o.Fan.SmallRpm ?? 0}");
        }
    }
    catch (Exception ex) { Log($"[{tag}] Fan target failed: {ex.Message}"); }
}

// ---- ParameterGuard: 60 秒周期性幂等重发计算类参数 ----
_ = System.Threading.Tasks.Task.Run(async () =>
{
    await System.Threading.Tasks.Task.Delay(10_000); // 启动后等 10 秒再开始
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
    try
    {
        while (await timer.WaitForNextTickAsync())
        {
            if (_pgSuppress)
            {
                AppLog.Write("ParameterGuard", "睡眠期间暂停，跳过本轮重发");
                continue;
            }
            try
            {
                await RestoreComputeSettings("ParameterGuard");
            }
            catch (Exception ex)
            {
                AppLog.Write("ParameterGuard", $"参数重发失败: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException) { /* 正常退出 */ }
});

// ---- 启动时恢复 GPU 模式 (异步，不阻塞服务启动) ----
_ = System.Threading.Tasks.Task.Run(() =>
{
    try
    {
        var saved = JsonRead<Dictionary<string, int>>("gpu-mode.json", new Dictionary<string, int>());
        bool hasSaved = saved.TryGetValue("gpuMode", out int mode) && mode >= 0 && mode <= 2;

        var wmiStartup = app.Services.GetRequiredService<WmiInterface>();
        // 最多重试 3 次，每次间隔 2 秒，等待 WMI 就绪
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (!wmiStartup.Available)
            {
                Log($"[Startup] WMI not available, retry {attempt}/3...");
                System.Threading.Thread.Sleep(2000);
                continue;
            }

            // 读取固件当前 GPU mode
            byte firmware = wmiStartup.GetGpuMode();
            Log($"[Startup] Firmware GPU mode={firmware}, saved={( hasSaved ? mode.ToString() : "none" )}");

            // 有存档且有效 → 恢复到存档值（尊重用户选择，包括 iGPU-only）
            if (hasSaved)
            {
                // 固件已与存档一致，无需切换
                if (firmware == mode)
                {
                    Log($"[Startup] GPU mode already at {mode}, no action needed");
                    return;
                }

                if (!wmiStartup.SetGpuMode((byte)mode))
                {
                    Log($"[Startup] SetGpuMode({mode}) failed, retry {attempt}/3...");
                    System.Threading.Thread.Sleep(2000);
                    continue;
                }
                byte current = wmiStartup.GetGpuMode();
                if (current == mode)
                {
                    Log($"[Startup] GPU mode restored to {mode} (verified)");
                    return;
                }
                Log($"[Startup] GPU mode mismatch: expected {mode}, got {current}, retry {attempt}/3...");
                System.Threading.Thread.Sleep(2000);
                continue;
            }

            // 无存档 → 不干预固件状态，仅记录
            Log($"[Startup] No saved GPU mode, firmware is at {firmware}, no action needed");
            return;
        }
        Log("[Startup] GPU mode restore failed after 3 attempts");
    }
    catch (Exception ex) { Log($"[Startup] GPU mode restore failed: {ex.Message}"); }
});
// ---- 启动时恢复性能设置 (异步，在 GPU 模式恢复之后) ----
_ = System.Threading.Tasks.Task.Run(async () =>
{
    await System.Threading.Tasks.Task.Delay(3000);
    await RestoreAllPerfSettings("Startup");
});
app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("需要 WebSocket 连接");
        return;
    }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    TelemetryBackgroundService.AddClient(ws);
    ProcessMonitorService.AddClient(ws);
    try
    {
        var buf = new byte[4096];
        while (ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = null;
            try { result = await ws.ReceiveAsync(buf, CancellationToken.None); }
            catch (WebSocketException) { break; }
            if (result != null && result.MessageType == WebSocketMessageType.Close) break;
        }
    }
    finally
    {
        TelemetryBackgroundService.RemoveClient(ws);
        ProcessMonitorService.RemoveClient(ws);
        if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { }
        }
    }
});
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
var _sysInfoExtCache = "";
var _sysInfoExtTime = DateTime.MinValue;
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
        using var p = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
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
app.MapPost("/api/control", (ControlRequest req, HardwareAbstractionLayer hal, WmiInterface wmi) =>
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
                SavePerfOverrides(o => o.PowerPlan = req.Value);
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
                    osdService.Show(modeNames[clampedMode]);
                    // 通知 ProcessMonitorService 更新当前模式
                    app.Services.GetRequiredService<ProcessMonitorService>().UpdateCurrentMode(modeNames[clampedMode]);
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
                    JsonWrite("gpu-mode.json", new { gpuMode = gpuVal });
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

// ---- 关闭显示器 ----
app.MapPost("/api/monitor/off", () =>
{
    NativeMethods.SendMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));
    return Results.Ok(new { ok = true });
});

// ---- 快捷键配置 ----
app.MapGet("/api/hotkey/monitor-off", () =>
{
    var cfgPath = Path.Combine(configDir, "hotkey-config.json");
    if (!File.Exists(cfgPath))
        return Results.Json(new { enabled = true, modifiers = "ctrl,shift", key = "Q", conflict = false });
    try
    {
        var json = File.ReadAllText(cfgPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var mo = root.TryGetProperty("monitorOff", out var moVal) ? moVal : root;
        bool conflict = false;
        var statusPath = Path.Combine(configDir, "hotkey-status.json");
        if (File.Exists(statusPath))
        {
            try
            {
                var sJson = File.ReadAllText(statusPath);
                using var sDoc = JsonDocument.Parse(sJson);
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

app.MapPost("/api/hotkey/monitor-off", (HotkeyConfigRequest req) =>
{
    var cfgPath = Path.Combine(configDir, "hotkey-config.json");
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
    JsonWrite("hotkey-config.json", existing);
    return Results.Ok(new { ok = true });
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
app.MapGet("/api/ec-scan", (HttpContext ctx, HardwareAbstractionLayer hal) =>
{
    if (LocalAccessGuard.BlockUnsafeTool("/api/ec-scan") is { } blocked) return blocked;
    var offsetStr = ctx.Request.Query["offset"].FirstOrDefault() ?? "0";
    var countStr = ctx.Request.Query["count"].FirstOrDefault() ?? "16";
    try
    {
        uint offset = offsetStr.StartsWith("0x") ? Convert.ToUInt32(offsetStr, 16) : uint.Parse(offsetStr);
        int count = int.Parse(countStr);
        count = Math.Clamp(count, 1, 64);
        if (offset + count > 0xFF) count = (int)(0xFF - offset);
        if (count <= 0) return Results.Json(new { error = "超出范围" }, statusCode: 400);
        var results = new List<object>();
        for (int i = 0; i < count; i++)
        {
            byte val = 0;
            try { val = hal.ReadEcPort((byte)(offset + i)); } catch { val = 0; }
            results.Add(new { offset = $"0x{offset + i:X2}", value = val });
        }
        return Results.Json(new { ecBase = "0xFE800400", offset, count, results });
    }
    catch (Exception ex)
    {
        return ApiProblem.BadRequest(ex, "/api/ec-scan");
    }
});
app.MapPost("/api/smu/set", (SmuController smu, SmuSetRequest req) =>
{
    try
    {
        int rc;
        switch (req.Parameter)
        {
            case "stapm_limit":
            case "power_limit":
                rc = smu.SetPowerLimit((uint)(req.ValueM * 1000));
                SavePerfOverrides(o => o.Smu.StapmLimitW = req.ValueM);
                break;
            case "short_power_limit":
                rc = smu.SetShortPowerLimit((uint)(req.ValueM * 1000), (uint)(req.ValueM * 1000));
                SavePerfOverrides(o => o.Smu.ShortPowerLimitW = req.ValueM);
                break;
            case "tctl_temp":
            case "temp_limit":
                rc = smu.SetTempLimit((uint)req.ValueM);
                SavePerfOverrides(o => o.Smu.TempLimitC = req.ValueM);
                break;
            case "co_all":
                rc = smu.SetCurveOptimizer(req.ValueM);
                SavePerfOverrides(o => o.Smu.CoAll = req.ValueM);
                break;
            case "turbo_disable":
                rc = smu.SetTurboDisabled(req.ValueM != 0);
                break;
            default:
                return Results.Json(new { ok = false, error = "unknown parameter: " + req.Parameter });
        }
        return Results.Json(new { ok = rc == 0, rc });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/smu/set");
    }
});
app.MapPost("/api/smu/raw", (SmuController smu, SmuRawRequest req) =>
{
    if (LocalAccessGuard.BlockUnsafeTool("/api/smu/raw") is { } blocked) return blocked;
    try
    {
        var resp = smu.SendRawSmuCommand(req.Cmd, req.Arg0);
        return Results.Json(new { ok = true, cmd = req.Cmd, arg0 = req.Arg0, response = resp });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/smu/raw");
    }
});
app.MapGet("/api/smu/probe", (SmuController smu) =>
{
    try
    {
        var ok = smu.Probe();
        return Results.Json(new { ok, source = "ryzenadj" });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/smu/probe");
    }
});
app.MapGet("/api/pci/probe", () =>
{
    if (LocalAccessGuard.BlockUnsafeTool("/api/pci/probe") is { } blocked) return blocked;
    try
    {
        var io = Douzhanzhe.HAL.DriverBridge.Instance;
        io.WriteIo32((short)0xCF8, unchecked((int)(0x80000000u | 0x00)));
        var vendorDevice = (uint)io.ReadIo32((short)0xCFC);
        var vendorId = vendorDevice & 0xFFFF;
        var deviceId = vendorDevice >> 16;
        return Results.Json(new { ok = true, vendorId = $"0x{vendorId:X4}", deviceId = $"0x{deviceId:X4}", isAmd = vendorId == 0x1022 });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/pci/probe");
    }
});
app.MapGet("/api/smu/status", (SmuController smu) =>
{
    try
    {
        var probe = smu.Probe();
        var caps = smu.GetCapabilities();
        return Results.Json(new { ok = true, probe, source = "ryzenadj", capabilities = caps });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/smu/status");
    }
});
app.MapGet("/api/smu/read-reg", (SmuController smu, HttpContext ctx) =>
{
    if (LocalAccessGuard.BlockUnsafeTool("/api/smu/read-reg") is { } blocked) return blocked;
    try
    {
        var addrStr = ctx.Request.Query["addr"].FirstOrDefault() ?? "0";
        addrStr = addrStr.Trim();
        if (addrStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            addrStr = addrStr.Substring(2);
        uint addr = Convert.ToUInt32(addrStr, 16);
        var value = smu.ReadSmnRegister(addr);
        return Results.Json(new { ok = true, addr = $"0x{addr:X}", value });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/smu/read-reg");
    }
});

// ---- Fan Curve (自定义散热曲线) ----
var _fanCurveSvc = app.Services.GetRequiredService<FanCurveService>();
_fanCurveSvc.LoadConfig(); // 启动时加载已保存的曲线

app.MapFanEndpoints();


// ---- NVAPI GPU 控制 (超频/降频/功率/温度) ----
var nvapi = app.Services.GetRequiredService<NvapiGpuController>();
if (!nvapi.Init())
    Log("[NVAPI] 未初始化，超频/功率/温度控制不可用");

app.MapGpuEndpoints();

app.MapCpuEndpoints();

// ---- Node.js 废弃迁移端点 ----
app.MapPost("/api/uxtu/apply", async (HttpContext ctx, SmuController smu) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var rawJson = await reader.ReadToEndAsync();
        Log($"[uxtu/apply] ← {rawJson}");
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var body = JsonSerializer.Deserialize<UxtuApplyRequest>(rawJson, jsonOpts);
        if (body == null) { Log("[uxtu/apply] ✗ invalid body"); return Results.Json(new { ok = false, error = "invalid body" }); }
        int? cpuPpt = body.Params?.CpuLongPptW ?? body.Limits?.Cpu?.PptLimitW;
        int? cpuShortPpt = body.Params?.CpuShortPptW;
        int? cpuTemp = body.Params?.CpuTempLimitC ?? body.Limits?.Cpu?.TempLimitC;
        int? cpuVoltage = body.Params?.CpuVoltageOffset;
        bool? cpuTurboOff = body.Params?.CpuTurboDisabled;
        int? cpuCoreLimit = body.Params?.CpuCoreLimit;
        // 批量单次 ryzenadj 调用（CPU 频率限制走 /api/cpu/freq-limit powercfg 路径，此处不再处理）
        uint? stapmMw = cpuPpt.HasValue ? (uint)(cpuPpt.Value * 1000) : null;
        uint? fastMw = cpuShortPpt.HasValue ? (uint)(cpuShortPpt.Value * 1000) : stapmMw;
        uint? slowMw = fastMw;
        uint? tempC = cpuTemp.HasValue ? (uint)cpuTemp.Value : null;
        int? coAllMv = cpuVoltage;
        bool? turboOff = cpuTurboOff;
        // turbo 统一走 powercfg 路径（与独立端点 /api/cpu/turbo 对齐），不通过 ryzenadj
        var rc = smu.BatchApply(stapmMw, fastMw, slowMw, tempC, coAllMv, null);
        if (cpuCoreLimit.HasValue) { CpuAffinityManager.SetCoreLimit(cpuCoreLimit.Value); }
        // CPU 频率限制 (powercfg 路径)
        if (body.Params?.CpuFreqLimitEnabled == true && body.Params.CpuFreqLimitMhz.HasValue && body.Params.CpuFreqLimitMhz.Value > 0)
        {
            try { await app.Services.GetRequiredService<CpuPowerController>().SetFreqLimitAsync(body.Params.CpuFreqLimitMhz.Value); } catch { }
        }
        else if (body.Params?.CpuFreqLimitEnabled == false)
        {
            try { await app.Services.GetRequiredService<CpuPowerController>().SetFreqLimitAsync(0); } catch { }
        }
        // Turbo 开关
        if (cpuTurboOff.HasValue)
        {
            try { await app.Services.GetRequiredService<CpuPowerController>().SetTurboAsync(!cpuTurboOff.Value); } catch { }
        }
        // 持久化全部 CPU / SMU 参数（与各独立端点对齐）
        SavePerfOverrides(o =>
        {
            // CPU
            if (body.Params?.CpuFreqLimitMhz.HasValue == true)
                o.Cpu.FreqLimitMhz = body.Params.CpuFreqLimitEnabled == true ? body.Params.CpuFreqLimitMhz : 0;
            if (cpuTurboOff.HasValue) o.Cpu.TurboEnabled = !cpuTurboOff.Value;
            if (cpuCoreLimit.HasValue) o.Cpu.CoreLimitPercent = cpuCoreLimit.Value > 0 ? (int)Math.Round(cpuCoreLimit.Value / 16.0 * 100) : 100;
            // SMU
            if (cpuPpt.HasValue) o.Smu.StapmLimitW = cpuPpt.Value;
            if (cpuShortPpt.HasValue) o.Smu.ShortPowerLimitW = cpuShortPpt.Value;
            if (cpuTemp.HasValue) o.Smu.TempLimitC = cpuTemp.Value;
            if (cpuVoltage.HasValue) o.Smu.CoAll = cpuVoltage.Value;
        });
        Log($"[uxtu/apply] ✓ saved ppt={cpuPpt} short={cpuShortPpt} temp={cpuTemp} co={cpuVoltage} freqLim={body.Params?.CpuFreqLimitMhz} turbo={cpuTurboOff} core={cpuCoreLimit} rc={rc}");
        return Results.Json(new { ok = rc == 0, message = rc == 0 ? "OK" : $"rc={rc}" });
    }
    catch (Exception ex) { Log($"[uxtu/apply] ✗ {ex.Message}"); return ApiProblem.From(ex, "/api/uxtu/apply"); }
});
app.MapGet("/api/ryzenadj/info", (SmuController smu) =>
{
    try
    {
        var probeOk = smu.Probe();
        return Results.Json(new { ok = probeOk, data = new { probeResult = probeOk, type = "subprocess", source = "ryzenadj" } });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/ryzenadj/info"); }
});
app.MapPost("/api/wmi/cmd", (WmiInterface wmi, WmiCmdRequest req) =>
{
    if (LocalAccessGuard.BlockUnsafeTool("/api/wmi/cmd") is { } blocked) return blocked;
    try
    {
        byte? value = req.Value.HasValue ? (byte?)req.Value.Value : null;
        var result = wmi.SendRawCommand((byte)req.Method, value);
        var outVal = result.Length > 4 ? (int?)result[4] : null;
        var hexResp = string.Join(" ", result.Take(8).Select(b => b.ToString("X2")));
        return Results.Json(new { ok = true, method = req.Method, value = req.Value, response = hexResp, outValue = outVal });
    }
    catch (Exception ex)
    {
        return ApiProblem.From(ex, "/api/wmi/cmd");
    }
});
app.MapPost("/api/system/settings", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = JsonSerializer.Deserialize<SystemSettingsRequest>(await reader.ReadToEndAsync());
        AppLog.Write("API", $"[system] {body?.Key}={body?.Value} — Node.js 已废弃，此端点仅做兼容");
        return Results.Json(new { ok = false, error = "此端点已废弃，请使用 /api/control" });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/system/settings"); }
});
app.MapPost("/api/fan/full-speed", () =>
{
    return Results.Json(new { ok = false, error = "此端点已废弃，请使用 /api/fan/set-target 手动控制风扇" });
});
// ---- 日志导出（供用户反馈问题时使用）----
app.MapGet("/api/logs/export", () =>
{
    var logFile = Path.Combine(_logDir, "app.log");
    if (!File.Exists(logFile))
        return Results.Json(new { ok = false, error = "日志文件不存在" });
    var bytes = File.ReadAllBytes(logFile);
    var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    return Results.File(bytes, "text/plain; charset=utf-8", $"douzhanzhe-log-{ts}.log");
});
app.MapGet("/api/smu/api-type", () =>
{
    return Results.Json(new { ok = true, type = "subprocess", source = "smucontroller->ryzenadj" });
});
app.MapGet("/api/ui-state", () =>
{
    return Results.Json(JsonRead<UiState>("ui-state.json", new UiState()));
});
app.MapPost("/api/ui-state", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var readOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var body = JsonSerializer.Deserialize<UiState>(await reader.ReadToEndAsync(), readOpts);
        JsonWrite("ui-state.json", body ?? new UiState());
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/ui-state"); }
});
app.MapGet("/api/default-config", () =>
{
    return Results.Json(JsonRead<DefaultConfig>("dashboard-default.json", new DefaultConfig()));
});
app.MapPost("/api/default-config", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var readOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var body = JsonSerializer.Deserialize<DefaultConfig>(await reader.ReadToEndAsync(), readOpts);
        JsonWrite("dashboard-default.json", body ?? new DefaultConfig());
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/default-config"); }
});

// ---- Auto-start options (minimized preference + enabled cache) ----
var autoStartOptsPath = Path.Combine(AppContext.BaseDirectory, "config", "auto-start-opts.json");
Directory.CreateDirectory(Path.GetDirectoryName(autoStartOptsPath)!);

// 读取本地缓存的 auto-start 状态（快速路径，无 COM 开销）
(bool enabled, bool minimized) ReadAutoStartOpts()
{
    try
    {
        if (File.Exists(autoStartOptsPath))
        {
            var json = File.ReadAllText(autoStartOptsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var en = root.TryGetProperty("enabled", out var ev) && ev.ValueKind == JsonValueKind.True;
            var min = root.TryGetProperty("minimized", out var mv) && mv.ValueKind == JsonValueKind.True;
            return (en, min);
        }
    }
    catch { }
    return (false, false);
}

void WriteAutoStartOpts(bool enabled, bool minimized)
{
    File.WriteAllText(autoStartOptsPath, JsonSerializer.Serialize(new { enabled, minimized }));
}

app.MapGet("/api/auto-start-opts", () =>
{
    var (_, minimized) = ReadAutoStartOpts();
    return Results.Json(new { minimized });
});
app.MapPost("/api/auto-start-opts", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await reader.ReadToEndAsync());
        if (body == null || !body.TryGetValue("minimized", out var v) || v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False)
            return Results.Json(new { ok = false, error = "需要 { minimized: bool }" });
        var minimized = v.GetBoolean();
        var (enabled, _) = ReadAutoStartOpts();
        WriteAutoStartOpts(enabled, minimized);
        return Results.Json(new { ok = true, minimized });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/auto-start-opts"); }
});

// ---- Auto-start (Windows Task Scheduler) ----
app.MapGet("/api/auto-start", () =>
{
    try
    {
        // 快速路径：先读本地缓存，立即返回
        var (cachedEnabled, _) = ReadAutoStartOpts();

        // 后台异步校验：查计划任务，不一致则修正缓存
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // 等 2 秒再查，避免安装后 Task Scheduler 尚未注册完毕导致误判
                Thread.Sleep(2000);
                using var ts = new TaskService();
                var actual = ts.RootFolder.AllTasks.Any(t => t.Name == "DouzhanzheControl");
                // 二次确认：若缓存为 true 但首次未找到，再等 2 秒重试
                if (!actual && cachedEnabled)
                {
                    Thread.Sleep(2000);
                    actual = ts.RootFolder.AllTasks.Any(t => t.Name == "DouzhanzheControl");
                }
                var (curEnabled, min) = ReadAutoStartOpts();
                if (actual != curEnabled)
                    WriteAutoStartOpts(actual, min);
            }
            catch { /* 校验失败不影响本次响应 */ }
        });

        return Results.Json(new { enabled = cachedEnabled });
    }
    catch { return Results.Json(new { enabled = false }); }
});
app.MapPost("/api/auto-start", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await reader.ReadToEndAsync());
        if (body == null || !body.TryGetValue("enabled", out var enabledEl) || enabledEl.ValueKind != JsonValueKind.True && enabledEl.ValueKind != JsonValueKind.False)
            return Results.Json(new { ok = false, error = "需要 { enabled: bool }" });
        var enabled = enabledEl.GetBoolean();

        using var ts = new TaskService();
        if (enabled)
        {
            // 定位 Shell.exe：同目录下查找，或 dev 路径回退
            var apiDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var shellExe = new[] { "Douzhanzhe.Shell.exe" }
                .Select(f => Path.Combine(apiDir, f))
                .FirstOrDefault(File.Exists);
            if (shellExe == null)
            {
                // 开发环境路径回退
                shellExe = Path.GetFullPath(Path.Combine(apiDir, "..", "..", "..", "..", "shell", "Douzhanzhe.Shell", "bin", "Debug", "net8.0-windows", "Douzhanzhe.Shell.exe"));
            }

            // 读取最小化偏好
            var (_, minimized) = ReadAutoStartOpts();

            var td = ts.NewTask();
            td.RegistrationInfo.Description = "Douzhanzhe Console 开机自启";
            td.Principal.RunLevel = TaskRunLevel.Highest;
            td.Settings.DisallowStartIfOnBatteries = false;
            td.Settings.StopIfGoingOnBatteries = false;
            td.Settings.DisallowStartOnRemoteAppSession = false;
            td.Triggers.Add(new LogonTrigger());
            td.Actions.Add(shellExe, minimized ? "--minimized" : "");
            ts.RootFolder.RegisterTaskDefinition("DouzhanzheControl", td);
        }
        else
        {
            if (ts.RootFolder.AllTasks.Any(t => t.Name == "DouzhanzheControl"))
                ts.RootFolder.DeleteTask("DouzhanzheControl");
        }

        // 同步写入本地缓存
        var (_, min) = ReadAutoStartOpts();
        WriteAutoStartOpts(enabled, min);

        return Results.Json(new { ok = true, enabled });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/auto-start"); }
});

// ---- Custom background image ----
var bgOptsPath = Path.Combine(configDir, "background-opts.json");
string[] BgImageFiles() => configStore.BackgroundImageFiles();

app.MapGet("/api/background-opts", () =>
{
    try
    {
        if (File.Exists(bgOptsPath))
        {
            var json = File.ReadAllText(bgOptsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var enabled = root.TryGetProperty("enabled", out var ev) && ev.ValueKind == JsonValueKind.True;
            var opacity = root.TryGetProperty("opacity", out var ov) ? Math.Clamp(ov.GetInt32(), 0, 100) : 50;
            var maskColor = root.TryGetProperty("maskColor", out var mv) && mv.GetString() == "white" ? "white" : "black";
            var hasImage = BgImageFiles().Length > 0;
            return Results.Json(new { enabled, opacity, maskColor, hasImage });
        }
        return Results.Json(new { enabled = false, opacity = 50, maskColor = "black", hasImage = false });
    }
    catch { return Results.Json(new { enabled = false, opacity = 50, maskColor = "black", hasImage = false }); }
});

app.MapPost("/api/background-opts", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await reader.ReadToEndAsync());
        if (body == null) return Results.Json(new { ok = false, error = "无效请求" });

        // 读取当前配置
        bool enabled = false; int opacity = 50; string maskColor = "black";
        if (File.Exists(bgOptsPath))
        {
            try
            {
                var old = JsonDocument.Parse(File.ReadAllText(bgOptsPath)).RootElement;
                enabled = old.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
                opacity = old.TryGetProperty("opacity", out var o) ? o.GetInt32() : 50;
                maskColor = old.TryGetProperty("maskColor", out var m) && m.GetString() == "white" ? "white" : "black";
            }
            catch { }
        }

        if (body.TryGetValue("enabled", out var ev)) enabled = ev.ValueKind == JsonValueKind.True;
        if (body.TryGetValue("opacity", out var ov)) opacity = Math.Clamp(ov.GetInt32(), 0, 100);
        if (body.TryGetValue("maskColor", out var mv)) maskColor = mv.GetString() == "white" ? "white" : "black";

        File.WriteAllText(bgOptsPath, JsonSerializer.Serialize(new { enabled, opacity, maskColor }));
        return Results.Json(new { ok = true, enabled, opacity, maskColor });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/background-opts"); }
});

app.MapPost("/api/background", async (HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await reader.ReadToEndAsync());
        if (body == null || !body.TryGetValue("image", out var imgEl))
            return Results.Json(new { ok = false, error = "需要 { image: base64dataUrl }" });

        var dataUrl = imgEl.GetString() ?? "";
        // 解析 data URL: "data:image/png;base64,xxxx"
        var commaIdx = dataUrl.IndexOf(',');
        if (commaIdx < 0) return Results.Json(new { ok = false, error = "无效的图片数据" });

        var meta = dataUrl[..commaIdx];
        var b64 = dataUrl[(commaIdx + 1)..];
        var ext = "png";
        if (meta.Contains("jpeg") || meta.Contains("jpg")) ext = "jpg";
        else if (meta.Contains("webp")) ext = "webp";

        // 清理旧的背景图片（只删图片文件，不碰 JSON 配置）
        foreach (var old in BgImageFiles())
        {
            try { File.Delete(old); }
            catch { /* 忽略被占用的文件，写入时会被覆盖 */ }
        }

        var filePath = Path.Combine(configDir, $"background.{ext}");
        var tmpPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, Convert.FromBase64String(b64));
        // 原子替换：先写临时文件，再重命名
        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tmpPath, filePath);
        return Results.Json(new { ok = true, ext });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/background"); }
});

app.MapGet("/api/background", async (HttpContext ctx) =>
{
    try
    {
        var files = BgImageFiles();
        if (files.Length == 0) return Results.NotFound();

        var filePath = files[0];
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };

        // HEAD 请求：Results.File(byte[]) 对 HEAD 不兼容，直接返回 200
        if (ctx.Request.Method == "HEAD")
            return Results.Ok();

        // 读入内存以释放文件句柄，避免与上传操作冲突
        var bytes = await File.ReadAllBytesAsync(filePath);
        return Results.File(bytes, contentType);
    }
    catch { return Results.StatusCode(500); }
});

app.MapDelete("/api/background", () =>
{
    try
    {
        foreach (var f in BgImageFiles())
            File.Delete(f);
        // 同时禁用
        int opacity = 50; string maskColor = "black";
        if (File.Exists(bgOptsPath))
        {
            try
            {
                var old = JsonDocument.Parse(File.ReadAllText(bgOptsPath)).RootElement;
                opacity = old.TryGetProperty("opacity", out var o) ? o.GetInt32() : 50;
                maskColor = old.TryGetProperty("maskColor", out var m) && m.GetString() == "white" ? "white" : "black";
            }
            catch { }
        }
        File.WriteAllText(bgOptsPath, JsonSerializer.Serialize(new { enabled = false, opacity, maskColor }));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ApiProblem.From(ex, "/api/background"); }
});

// ---- 检查更新 (GitHub Releases API) ----
var _updateHttpClient = new HttpClient();
_updateHttpClient.Timeout = TimeSpan.FromSeconds(8);
_updateHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DouzhanzheConsole-UpdateChecker/1.0");

// 从前端 JS bundle 提取版本号（构建时 SettingsPanel.jsx 中的 "Douzhanzhe Console vX.Y.Z"）
// 注意：覆盖安装时 wwwroot/assets 可能残留多个旧 bundle，必须遍历所有文件取最大版本号
var _appVersion = "0.0.0";
try
{
    var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets");
    if (Directory.Exists(wwwroot))
    {
        var jsFiles = Directory.GetFiles(wwwroot, "index-*.js");
        var maxVer = new Version(0, 0, 0);
        foreach (var jsFile in jsFiles)
        {
            try
            {
                var jsContent = File.ReadAllText(jsFile);
                var m = System.Text.RegularExpressions.Regex.Match(jsContent, @"Douzhanzhe Console v(\d+\.\d+\.\d+)");
                if (m.Success && Version.TryParse(m.Groups[1].Value, out var v) && v > maxVer)
                    maxVer = v;
            }
            catch { /* 单个文件读取失败不影响其他 */ }
        }
        if (maxVer > new Version(0, 0, 0))
            _appVersion = maxVer.ToString();
    }
}
catch { /* 读取失败时使用默认值 */ }

app.MapGet("/api/update/check", async () =>
{
    try
    {
        var CurrentVersion = _appVersion;
        var res = await _updateHttpClient.GetAsync(
            "https://api.github.com/repos/KanzakiK/DOUZHANZHE-Control/releases/latest");

        // 无 release (404) 或网络故障 → 视为无更新
        if (!res.IsSuccessStatusCode)
            return Results.Json(new { available = false, currentVersion = CurrentVersion,
                reason = "无法获取发布信息" });

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var latestVersion = tag.TrimStart('v');
        var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
        var publishedAt = root.TryGetProperty("published_at", out var p) ? p.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

        var isNewer = false;
        if (Version.TryParse(latestVersion, out var latest) &&
            Version.TryParse(CurrentVersion, out var current))
        {
            isNewer = latest > current;
        }

        return Results.Json(new
        {
            available = isNewer,
            currentVersion = CurrentVersion,
            latestVersion,
            body,
            publishedAt,
            url = htmlUrl
        });
    }
    catch (Exception ex)
    {
        // 保持 200 与原有字段：前端 UpdateDialog 依赖 available / currentVersion，
        // 且"检查失败"属于可预期结果而非服务端故障。
        AppLog.Write("API", $"[/api/update/check] 检查更新失败: {ex.GetType().Name}: {ex.Message}");
        return Results.Json(new { available = false, currentVersion = _appVersion,
            error = "检查更新失败，请稍后重试" });
    }
});

app.MapGet("/debug", () => Results.File("wwwroot/debug.html", "text/html"));
// ---- 预启动 inpoutx64 内核驱动 ----
// 必须在任何 inpoutx64.dll 的 DllImport 调用之前执行！
// 原因：inpoutx64.dll 的 DllMain 在首次加载时会尝试打开 \\.\InpOut64 设备，
//       如果此时服务未运行，内部变量 bInpOutDriverOpened 会被永久设为 false，
//       即使之后启动了服务，IsInpOutDriverOpen() 也永远返回 false。
try
{
    var inpCheck = Process.Start(new ProcessStartInfo("sc.exe", "query inpoutx64") { UseShellExecute = false, CreateNoWindow = true });
    inpCheck?.WaitForExit(2000);
    if (inpCheck?.ExitCode != 0)
    {
        Log("[inpoutx64] 驱动未运行，尝试启动...");
        // 确保启动类型为 AUTO_START（下次开机自动加载）
        var cfgSvc = Process.Start(new ProcessStartInfo("sc.exe", "config inpoutx64 start=auto") { UseShellExecute = false, CreateNoWindow = true });
        cfgSvc?.WaitForExit(2000);
        // 立即启动驱动服务
        var startSvc = Process.Start(new ProcessStartInfo("sc.exe", "start inpoutx64") { UseShellExecute = false, CreateNoWindow = true });
        startSvc?.WaitForExit(3000);
        // 验证
        var verify = Process.Start(new ProcessStartInfo("sc.exe", "query inpoutx64") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        if (verify != null)
        {
            var outText = verify.StandardOutput.ReadToEnd();
            verify.WaitForExit(1000);
            if (outText.Contains("RUNNING"))
                Log("[inpoutx64] 驱动启动成功");
            else
                Log("[inpoutx64] 驱动启动可能失败: " + outText.Trim());
        }
    }
    else Log("[inpoutx64] 驱动已在运行");
}
catch (Exception ex) { Log("[inpoutx64] 预启动异常: " + ex.Message); }

// ---- Auto-load WinRing0 kernel driver for SMU ----
try
{
    var svcName = "WinRing0_1_2_0";
    var sysPath = Path.Combine(AppContext.BaseDirectory, "WinRing0x64.sys");
    if (File.Exists(sysPath))
    {
        var check = Process.Start(new ProcessStartInfo("sc.exe", "query " + svcName) { UseShellExecute = false, CreateNoWindow = true });
        check?.WaitForExit(2000);
        if (check?.ExitCode != 0)
        {
            Log("[WinRing0] Driver not loaded, attempting to install...");
            var create = Process.Start(new ProcessStartInfo("sc.exe", $"create {svcName} type=kernel start=demand binPath=\"{sysPath}\"") { UseShellExecute = false, CreateNoWindow = true });
            create?.WaitForExit(2000);
            var start = Process.Start(new ProcessStartInfo("sc.exe", "start " + svcName) { UseShellExecute = false, CreateNoWindow = true });
            start?.WaitForExit(2000);
            var verify = Process.Start(new ProcessStartInfo("sc.exe", "query " + svcName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (verify != null)
            {
                var outText = verify.StandardOutput.ReadToEnd();
                verify.WaitForExit(1000);
                if (outText.Contains("RUNNING"))
                    Log("[WinRing0] Driver loaded OK");
                else
                    Log("[WinRing0] Driver load FAILED - SMU control unavailable");
            }
        }
        else Log("[WinRing0] Driver already loaded");
    }
    else Log("[WinRing0] WinRing0x64.sys not found at " + sysPath);
}
catch (Exception ex) { Log("[WinRing0] Error: " + ex.Message); }

// ---- LHM 初始化（WinRing0 加载之后）----
LhmSensor.Open();

// ---- DriverBridge 冷启动重试（安全网） ----
// 正常情况下 inpoutx64 已在预启动阶段启动，这里只是兜底
if (!DriverBridge.Instance.Ready)
{
    Log("[DriverBridge] inpoutx64 首次初始化未成功，尝试最后补救...");
    try
    {
        var startSvc = Process.Start(new ProcessStartInfo("sc.exe", "start inpoutx64") { UseShellExecute = false, CreateNoWindow = true });
        startSvc?.WaitForExit(3000);
    }
    catch (Exception ex) { Log($"[DriverBridge] inpoutx64 补救异常: {ex.Message}"); }
    Log("[DriverBridge] 重试初始化，等待最多 5 秒...");
    DriverBridge.Instance.RetryInit(5000);
    Log($"[DriverBridge] 重试结果: Ready={DriverBridge.Instance.Ready}");
}

// ---- OSD API ----
app.MapPost("/api/osd/show", (OsdShowRequest req, OsdService osd) =>
{
    if (!string.IsNullOrWhiteSpace(req.Text))
        osd.Show(req.Text);
    return Results.Ok();
});

app.MapGameProfileEndpoints();

// ---- Start server ----
try
{
    Log("Starting server on http://127.0.0.1:3100");
    app.Run();
}
catch (Exception ex)
{
    Log($"[FATAL] Server failed to start: {ex.GetType().Name}: {ex.Message}");
    Log($"  StackTrace: {ex.StackTrace}");
    throw;
}
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
