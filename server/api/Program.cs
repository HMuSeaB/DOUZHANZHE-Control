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
app.MapSystemEndpoints(_logDir);
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

app.MapSmuEndpoints();
app.MapUiStateEndpoints();

app.MapAutoStartEndpoints();

app.MapBackgroundEndpoints();

app.MapUpdateEndpoints();

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
