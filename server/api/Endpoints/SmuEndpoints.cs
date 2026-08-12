using System.Text.Json;
using Douzhanzhe.HAL;

namespace Douzhanzhe.API.Endpoints;

/// <summary>
/// SMU（功耗墙/温度墙/曲线优化器）与底层硬件探测。
///
/// 其中 smu/raw、pci/probe、ec-scan、smu/read-reg、wmi/cmd 是逆向调试用的裸硬件
/// 通道，默认由 LocalAccessGuard 关闭，需显式解锁。
/// </summary>
public static class SmuEndpoints
{
    static void Log(string msg) => AppLog.Write("API", msg);

    public static void MapSmuEndpoints(this WebApplication app)
    {
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

        app.MapPost("/api/smu/set", (SmuController smu, ConfigStore config, SmuSetRequest req) =>
        {
            try
            {
                int rc;
                switch (req.Parameter)
                {
                    case "stapm_limit":
                    case "power_limit":
                        rc = smu.SetPowerLimit((uint)(req.ValueM * 1000));
                        config.SavePerfOverrides(o => o.Smu.StapmLimitW = req.ValueM);
                        break;
                    case "short_power_limit":
                        rc = smu.SetShortPowerLimit((uint)(req.ValueM * 1000), (uint)(req.ValueM * 1000));
                        config.SavePerfOverrides(o => o.Smu.ShortPowerLimitW = req.ValueM);
                        break;
                    case "tctl_temp":
                    case "temp_limit":
                        rc = smu.SetTempLimit((uint)req.ValueM);
                        config.SavePerfOverrides(o => o.Smu.TempLimitC = req.ValueM);
                        break;
                    case "co_all":
                        rc = smu.SetCurveOptimizer(req.ValueM);
                        config.SavePerfOverrides(o => o.Smu.CoAll = req.ValueM);
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
                var io = DriverBridge.Instance;
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

        app.MapGet("/api/smu/api-type", () =>
        {
            return Results.Json(new { ok = true, type = "subprocess", source = "smucontroller->ryzenadj" });
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

        MapUxtuApply(app);
    }

    /// <summary>Node.js 后端遗留的批量下发入口，一次调用同时落 SMU 与 powercfg 两条路径。</summary>
    static void MapUxtuApply(WebApplication app)
    {
        app.MapPost("/api/uxtu/apply", async (HttpContext ctx, SmuController smu, CpuPowerController cpuPower, ConfigStore config) =>
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
                // turbo 统一走 powercfg 路径（与独立端点 /api/cpu/turbo 对齐），不通过 ryzenadj
                var rc = smu.BatchApply(stapmMw, fastMw, slowMw, tempC, coAllMv, null);
                if (cpuCoreLimit.HasValue) { CpuAffinityManager.SetCoreLimit(cpuCoreLimit.Value); }
                // CPU 频率限制 (powercfg 路径)
                if (body.Params?.CpuFreqLimitEnabled == true && body.Params.CpuFreqLimitMhz.HasValue && body.Params.CpuFreqLimitMhz.Value > 0)
                {
                    try { await cpuPower.SetFreqLimitAsync(body.Params.CpuFreqLimitMhz.Value); } catch { }
                }
                else if (body.Params?.CpuFreqLimitEnabled == false)
                {
                    try { await cpuPower.SetFreqLimitAsync(0); } catch { }
                }
                // Turbo 开关
                if (cpuTurboOff.HasValue)
                {
                    try { await cpuPower.SetTurboAsync(!cpuTurboOff.Value); } catch { }
                }
                // 持久化全部 CPU / SMU 参数（与各独立端点对齐）
                config.SavePerfOverrides(o =>
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
    }
}
