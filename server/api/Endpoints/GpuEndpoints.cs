using Douzhanzhe.HAL;

namespace Douzhanzhe.API.Endpoints;

/// <summary>
/// GPU 频率控制走两条互补路径：nvidia-smi 子进程负责锁频与显存档位，
/// NVAPI 负责超频偏移、功率墙与温度墙。
/// </summary>
public static class GpuEndpoints
{
    public static void MapGpuEndpoints(this WebApplication app)
    {
        MapNvidiaSmiEndpoints(app);
        MapNvapiEndpoints(app);
    }

    // ---- GPU 控制 (nvidia-smi 子进程) ----
    static void MapNvidiaSmiEndpoints(WebApplication app)
    {
        app.MapPost("/api/gpu/set", (GpuController gpu, ConfigStore config, GpuSetRequest req) =>
        {
            try
            {
                switch (req.Action)
                {
                    // 上限限制: --lock-gpu-clocks=0,value (仅传 value 时自动补 min=0)
                    case "lock":
                    case "lock-clocks":
                        if (req.Min.HasValue || req.Max.HasValue)
                            gpu.SetLockGpuClocks(req.Min ?? 0, req.Max ?? 0);
                        else
                            gpu.SetMaxGpuClock(req.Value ?? 0);
                        break;
                    // 精确锁定: --lock-gpu-clocks=min,min (单值锁频)
                    case "lock-exact":
                        gpu.SetExactGpuClock(req.Value ?? 0);
                        break;
                    // 上限限制 (显式): --lock-gpu-clocks=0,max
                    case "limit":
                    case "limit-max":
                        gpu.SetMaxGpuClock(req.Value ?? req.Max ?? 0);
                        break;
                    // 重置核心频率
                    case "reset":
                    case "reset-clocks":
                        gpu.ResetGpuClocks();
                        break;
                    // 显存区间锁定
                    case "lock-memory":
                    case "lock-memory-clocks":
                        gpu.SetLockMemoryClocks(req.Min ?? 0, req.Max ?? 0);
                        break;
                    // 显存上限限制
                    case "limit-memory":
                        gpu.SetMaxMemoryClock(req.Value ?? req.Max ?? 0);
                        break;
                    // 重置显存频率
                    case "reset-memory":
                    case "reset-memory-clocks":
                        gpu.ResetMemoryClocks();
                        break;
                    default:
                        return Results.Json(new { ok = false, error = "unknown action: " + req.Action });
                }
                // 持久化 GPU 控制设置 (nvidia-smi 路径)
                config.SavePerfOverrides(o =>
                {
                    switch (req.Action)
                    {
                        case "limit-max" or "limit":
                            o.Gpu.CoreFreqMhz = req.Value ?? req.Max ?? 0;
                            if (o.Gpu.FreqLocked != true) { /* 未锁定时不改变 locked 状态 */ }
                            break;
                        case "lock-exact":
                            o.Gpu.CoreFreqMhz = req.Value ?? 0;
                            o.Gpu.FreqLocked = true;
                            break;
                        case "reset-clocks" or "reset":
                            o.Gpu.CoreFreqMhz = null;
                            o.Gpu.FreqLocked = null;
                            break;
                        case "limit-memory":
                            // 前端传绝对值 9001/11001/12001，转换为 1/2/3 档位
                            var memMap = new Dictionary<int, int> { [9001] = 1, [11001] = 2, [12001] = 3 };
                            var val = req.Value ?? req.Max ?? 0;
                            o.Gpu.MemFreqLevel = memMap.TryGetValue(val, out var lvl) ? lvl : 0;
                            break;
                        case "reset-memory-clocks" or "reset-memory":
                            o.Gpu.MemFreqLevel = null;
                            break;
                    }
                });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/gpu/set");
            }
        });

        app.MapGet("/api/gpu/status", (GpuController gpu) =>
        {
            try
            {
                var info = gpu.GetClockInfo();
                var baseClock = gpu.GetBaseClock();
                var maxClock = gpu.GetMaxClock();
                return Results.Json(new { ok = true, coreClockMHz = info.CoreClockMHz, memoryClockMHz = info.MemoryClockMHz, powerDrawW = info.PowerDrawW, baseCoreClockMHz = baseClock, maxCoreClockMHz = maxClock });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/gpu/status");
            }
        });
    }

    // ---- NVAPI GPU 控制 (超频/降频/功率/温度) ----
    static void MapNvapiEndpoints(WebApplication app)
    {
        app.MapGet("/api/nvapi/status", (NvapiGpuController nv) =>
        {
            var s = nv.GetStatus();
            return Results.Json(new
            {
                ok = s.Available, gpuName = s.GpuName, overclockSupported = s.OverclockSupported, ocEngine = s.OcEngine,
                coreMhz = s.CoreMhz, memMhz = s.MemMhz,
                coreOffsetMhz = s.CoreOffsetMhz, memOffsetMhz = s.MemOffsetMhz,
                powerLimitMw = s.PowerLimitMw, powerMinMw = s.PowerMinMw,
                powerMaxMw = s.PowerMaxMw, powerDefaultMw = s.PowerDefaultMw,
                thermalLimitC = s.ThermalLimitC, thermalMinC = s.ThermalMinC,
                thermalMaxC = s.ThermalMaxC, thermalDefaultC = s.ThermalDefaultC
            });
        });

        app.MapGet("/api/nvapi/dump-pstates", (NvapiGpuController nv) =>
            Results.Text(nv.DumpPStates(), "text/plain"));

        app.MapPost("/api/nvapi/overclock", (NvapiGpuController nv, ConfigStore config, NvapiOverclockRequest req) =>
        {
            if (!nv.IsAvailable) return Results.Json(new { ok = false, error = "NVAPI not available" });
            var rc = nv.SetP0Offset(req.CoreOffsetMhz, req.MemOffsetMhz);
            config.SavePerfOverrides(o => { o.Nvapi.OcCoreOffsetMhz = req.CoreOffsetMhz; o.Nvapi.OcMemOffsetMhz = req.MemOffsetMhz; });
            return Results.Json(new { ok = rc == 0, rc });
        });

        app.MapPost("/api/nvapi/power-limit", (NvapiGpuController nv, ConfigStore config, NvapiPowerLimitRequest req) =>
        {
            if (!nv.IsAvailable) return Results.Json(new { ok = false, error = "NVAPI not available" });
            var rc = nv.SetPowerLimit((uint)(req.PowerW * 1000)); // W → mW
            config.SavePerfOverrides(o => o.Nvapi.PowerLimitW = req.PowerW);
            return Results.Json(new { ok = rc == 0, rc });
        });

        app.MapPost("/api/nvapi/thermal-limit", (NvapiGpuController nv, ConfigStore config, NvapiThermalLimitRequest req) =>
        {
            if (!nv.IsAvailable) return Results.Json(new { ok = false, error = "NVAPI not available" });
            var rc = nv.SetThermalLimit(req.TempC);
            config.SavePerfOverrides(o => o.Nvapi.ThermalLimitC = req.TempC);
            return Results.Json(new { ok = rc == 0, rc });
        });
    }
}
