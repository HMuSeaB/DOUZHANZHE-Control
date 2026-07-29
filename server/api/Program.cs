using Douzhanzhe.HAL;
using Douzhanzhe.API;
using System.Net.WebSockets;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<HardwareAbstractionLayer>();
builder.Services.AddHostedService<TelemetryBackgroundService>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

// ================================================================
// WebSocket — 遥测实时推送
// ================================================================
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
    try
    {
        // 保持连接直至关闭
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
        if (ws.State == WebSocketState.Open ||
            ws.State == WebSocketState.CloseReceived)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
            catch { }
        }
    }
});

// ================================================================
// GET /api/telemetry — 遥测大盘
// ================================================================
app.MapGet("/api/telemetry", (HardwareAbstractionLayer hal) =>
{
    return Results.Json(new
    {
        cpuTemp = hal.CpuTemperature,
        gpuTemp = hal.GpuTemperature,
        cpuFanRpm = hal.CpuFanRpm,
        gpuFanRpm = hal.GpuFanRpm,
        kbBrightness = hal.KeyboardBrightness,
        fnLock = hal.FnLock,
        numLock = hal.NumLock,
        capsLock = hal.CapsLock,
        thermalMode = hal.ThermalMode,
    });
});

// ================================================================
// GET /api/health — 健康检查
// ================================================================
app.MapGet("/api/health", (HardwareAbstractionLayer hal) =>
{
    return Results.Json(new
    {
        ok = hal.HealthCheck(),
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    });
});

// ================================================================
// POST /api/control — 硬件控制
// ================================================================
app.MapPost("/api/control", (ControlRequest req, HardwareAbstractionLayer hal) =>
{
    try
    {
        switch (req.Target)
        {
            case "kb_light":
                hal.KeyboardBrightness = (byte)int.Clamp(req.Value, 0, 3);
                break;
            case "fn_lock":
                hal.FnLock = req.Value != 0;
                break;
            case "num_lock":
                hal.NumLock = req.Value != 0;
                break;
            case "caps_lock":
                hal.CapsLock = req.Value != 0;
                break;
            case "thermal_mode":
                hal.ThermalMode = (byte)int.Clamp(req.Value, 0, 2);
                break;
            default:
                return Results.Problem($"未知控制目标: {req.Target}", statusCode: 400);
        }
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// ================================================================
// GET /api/discover — 探测
// ================================================================
app.MapGet("/api/discover", (HardwareAbstractionLayer hal) =>
{
    return Results.Json(new
    {
        available = hal.HealthCheck(),
        ecBase = $"0x{DriverBridge.EC_BASE:X}",
        driverLoaded = DriverBridge.Instance.Ready,
    });
});

app.Run();

record ControlRequest(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("value")] int Value
);
