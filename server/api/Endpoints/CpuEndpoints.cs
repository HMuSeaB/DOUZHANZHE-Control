using Douzhanzhe.HAL;

namespace Douzhanzhe.API.Endpoints;

/// <summary>CPU 睿频、频率上限与核心数控制（经 powercfg 电源计划）。</summary>
public static class CpuEndpoints
{
    static void Log(string msg) => AppLog.Write("API", msg);

    public static void MapCpuEndpoints(this WebApplication app)
    {
        app.MapGet("/api/cpu/status", (CpuPowerController cpu) =>
        {
            try
            {
                var s = cpu.GetStatus();
                return Results.Json(new
                {
                    ok = s.Available,
                    turboEnabled = s.TurboEnabled,
                    coreLimitPercent = s.CoreLimitPercent,
                    freqLimitMhz = s.FreqLimitMhz
                });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/cpu/status");
            }
        });

        app.MapPost("/api/cpu/freq-limit", async (CpuPowerController cpu, ConfigStore config, CpuFreqLimitRequest req) =>
        {
            try
            {
                Log($"[cpu/freq-limit] ← mhz={req.Mhz}");
                await cpu.SetFreqLimitAsync(req.Mhz);
                config.SavePerfOverrides(o => o.Cpu.FreqLimitMhz = req.Mhz);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                Log($"[cpu/freq-limit] ✗ {ex.Message}");
                return ApiProblem.From(ex, "/api/cpu/freq-limit");
            }
        });

        app.MapPost("/api/cpu/turbo", async (CpuPowerController cpu, ConfigStore config, CpuTurboRequest req) =>
        {
            try
            {
                Log($"[cpu/turbo] ← enabled={req.Enabled}");
                await cpu.SetTurboAsync(req.Enabled);
                config.SavePerfOverrides(o => o.Cpu.TurboEnabled = req.Enabled);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                Log($"[cpu/turbo] ✗ {ex.Message}");
                return ApiProblem.From(ex, "/api/cpu/turbo");
            }
        });

        app.MapPost("/api/cpu/core-limit", async (CpuPowerController cpu, ConfigStore config, CpuCoreLimitRequest req) =>
        {
            try
            {
                Log($"[cpu/core-limit] ← percent={req.Percent}");
                await cpu.SetCoreLimitAsync(req.Percent);
                config.SavePerfOverrides(o => o.Cpu.CoreLimitPercent = req.Percent);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                Log($"[cpu/core-limit] ✗ {ex.Message}");
                return ApiProblem.From(ex, "/api/cpu/core-limit");
            }
        });

        app.MapPost("/api/cpu/reset", async (CpuPowerController cpu, ConfigStore config) =>
        {
            try
            {
                await cpu.ResetAllAsync();
                config.SavePerfOverrides(o => { o.Cpu = new CpuOverrides(); });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/cpu/reset");
            }
        });
    }
}
