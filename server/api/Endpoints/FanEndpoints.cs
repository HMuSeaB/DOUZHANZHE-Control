using Douzhanzhe.HAL;

namespace Douzhanzhe.API.Endpoints;

/// <summary>
/// 风扇转速直写（WMI Bellator 协议）与自定义散热曲线的启停、保存、诊断。
/// </summary>
public static class FanEndpoints
{
    static void Log(string msg) => AppLog.Write("API", msg);

    public static void MapFanEndpoints(this WebApplication app)
    {
        MapFanControlEndpoints(app);
        MapFanCurveEndpoints(app);
    }

    static void MapFanControlEndpoints(WebApplication app)
    {
        app.MapPost("/api/fan/set-target", (FanSetRequest req, WmiInterface wmi, ConfigStore config) =>
        {
            try
            {
                Log($"[fan/set-target] ← large={req.LargeRpm} small={req.SmallRpm}");
                // Bellator 协议: 交错式 — Switch(fan) → Speed(fan) 逐扇操作
                if (req.LargeRpm.HasValue)
                {
                    var speed = (byte)Math.Clamp(req.LargeRpm.Value / 100, 0, 44);
                    wmi.SetFanManual(0, true);
                    wmi.SetFanSpeed(0, speed); // FanType 0 = CPUGPUFan
                }
                if (req.SmallRpm.HasValue)
                {
                    var speed = (byte)Math.Clamp(req.SmallRpm.Value / 100, 0, 82);
                    wmi.SetFanManual(1, true);
                    wmi.SetFanSpeed(1, speed); // FanType 1 = SYSFan
                }
                // 持久化固定风扇转速，供睡眠恢复 + 启动恢复使用
                config.SavePerfOverrides(o =>
                {
                    if (req.LargeRpm.HasValue) o.Fan.LargeRpm = req.LargeRpm.Value;
                    if (req.SmallRpm.HasValue) o.Fan.SmallRpm = req.SmallRpm.Value;
                });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan/set-target");
            }
        });

        // ---- Fan write strategy test (compare manual flag behavior) ----
        app.MapPost("/api/fan/test-write", (FanTestWriteRequest req, WmiInterface wmi) =>
        {
            if (LocalAccessGuard.BlockUnsafeTool("/api/fan/test-write") is { } blocked) return blocked;
            try
            {
                var strategy = req.Strategy ?? "manual-true";
                var largeSpeed = (byte)Math.Clamp((req.LargeRpm ?? 2900) / 100, 0, 44);
                var smallSpeed = (byte)Math.Clamp((req.SmallRpm ?? 6900) / 100, 0, 82);

                switch (strategy)
                {
                    case "manual-true":
                        // Current approach: Manual(true) + Speed, interleaved
                        wmi.SetFanManual(0, true);
                        wmi.SetFanSpeed(0, largeSpeed);
                        wmi.SetFanManual(1, true);
                        wmi.SetFanSpeed(1, smallSpeed);
                        break;

                    case "speed-only":
                        // No manual flag change, just speed writes
                        wmi.SetFanSpeed(0, largeSpeed);
                        wmi.SetFanSpeed(1, smallSpeed);
                        break;

                    case "manual-false":
                        // Set manual to false first, then speed
                        wmi.SetFanManual(0, false);
                        wmi.SetFanSpeed(0, largeSpeed);
                        wmi.SetFanManual(1, false);
                        wmi.SetFanSpeed(1, smallSpeed);
                        break;

                    case "speed-then-manual":
                        // Speed first, then manual (reversed order)
                        wmi.SetFanSpeed(0, largeSpeed);
                        wmi.SetFanSpeed(1, smallSpeed);
                        wmi.SetFanManual(0, true);
                        wmi.SetFanManual(1, true);
                        break;

                    default:
                        return Results.Json(new { ok = false, error = "Unknown strategy: " + strategy });
                }

                return Results.Json(new { ok = true, strategy, largeSpeed, smallSpeed });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan/test-write");
            }
        });

        app.MapPost("/api/fan/restore", (WmiInterface wmi, ConfigStore config) =>
        {
            try
            {
                wmi.SetFanManual(0, false);
                wmi.SetFanManual(1, false);
                // 清除持久化的风扇转速
                config.SavePerfOverrides(o => { o.Fan.LargeRpm = null; o.Fan.SmallRpm = null; });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan/restore");
            }
        });

        // ---- Fan status read (WMI Bellator GET) ----
        app.MapGet("/api/fan/status", (WmiInterface wmi) =>
        {
            try
            {
                var manualEnabled = wmi.GetFanManualEnabled();
                var largeTarget = wmi.GetFanSpeed(0) * 100;
                var smallTarget = wmi.GetFanSpeed(1) * 100;
                return Results.Json(new { ok = true, manualEnabled, largeRpmTarget = (int)largeTarget, smallRpmTarget = (int)smallTarget });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan/status");
            }
        });
    }

    // ---- Fan Curve (自定义散热曲线) ----
    static void MapFanCurveEndpoints(WebApplication app)
    {
        app.MapGet("/api/fan-curve/status", (FanCurveService svc) =>
        {
            return Results.Json(new
            {
                ok = true,
                active = svc.Active,
                points = svc.Points.Select(p => new { temp = p.Temp, largeRpm = p.LargeRpm, smallRpm = p.SmallRpm }),
            });
        });

        app.MapPost("/api/fan-curve/save", (FanCurveService svc, FanCurveSaveRequest req) =>
        {
            try
            {
                if (req.Points == null || req.Points.Count < 2)
                    return Results.Json(new { ok = false, error = "至少需要 2 个曲线点" });
                var points = req.Points.Select(p => new FanCurvePoint(p.Temp, p.LargeRpm, p.SmallRpm)).ToList();
                svc.SetPoints(points, req.IntervalMs, req.HysteresisC);
                svc.SaveConfig();
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan-curve/save");
            }
        });

        app.MapPost("/api/fan-curve/start", (FanCurveService svc, FanCurveStartRequest? req) =>
        {
            try
            {
                svc.Start(req?.IntervalMs, req?.HysteresisC);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan-curve/start");
            }
        });

        app.MapPost("/api/fan-curve/stop", (FanCurveService svc) =>
        {
            try
            {
                svc.Stop();
                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return ApiProblem.From(ex, "/api/fan-curve/stop");
            }
        });

        // ---- Fan Curve Route Info (路由状态查询) ----
        app.MapGet("/api/fan-curve/route-info", (FanCurveService svc) =>
        {
            return Results.Json(new
            {
                ok = true,
                active = svc.Active,
                currentItsm = svc.CurrentItsm,
                routedMode = svc.RoutedMode,
                lastLargeTarget = svc.LastLargeTarget,
                lastSmallTarget = svc.LastSmallTarget,
                itsmDeviationCount = svc.ItsmDeviationCount,
                // EC 读回诊断
                actualCpuFanRpm = svc.ActualCpuFanRpm,
                actualGpuFanRpm = svc.ActualGpuFanRpm,
                ecFanTargetLarge = svc.EcFanTargetLarge,
                ecFanTargetSmall = svc.EcFanTargetSmall,
                ecFanShadowLarge = svc.EcFanShadowLarge,
                ecFanShadowSmall = svc.EcFanShadowSmall,
                lastWmiLargeOk = svc.LastWmiLargeOk,
                lastWmiSmallOk = svc.LastWmiSmallOk,
                tickCount = svc.TickCount,
                consecutiveDeviation = svc.ConsecutiveDeviation,
                cpuTemp = svc.LastCpuTemp,
                gpuTemp = svc.LastGpuTemp,
                hotspot = svc.LastHotspot,
            });
        });
    }
}
