using System.Text.Json;
using Microsoft.Win32.TaskScheduler;

namespace Douzhanzhe.API.Endpoints;

/// <summary>
/// 开机自启（Windows 计划任务）及其最小化偏好。
///
/// 计划任务查询要走 COM，开销不小，因此启用状态另存一份本地缓存供快速读取，
/// 由后台校验保持一致。
/// </summary>
public static class AutoStartEndpoints
{
    const string TaskName = "DouzhanzheControl";

    static readonly string OptsPath =
        Path.Combine(AppContext.BaseDirectory, "config", "auto-start-opts.json");

    public static void MapAutoStartEndpoints(this WebApplication app)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OptsPath)!);

        MapOptionEndpoints(app);
        MapTaskEndpoints(app);
    }

    // ---- Auto-start options (minimized preference + enabled cache) ----
    static void MapOptionEndpoints(WebApplication app)
    {
        app.MapGet("/api/auto-start-opts", () =>
        {
            var (_, minimized) = ReadOpts();
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
                var (enabled, _) = ReadOpts();
                WriteOpts(enabled, minimized);
                return Results.Json(new { ok = true, minimized });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/auto-start-opts"); }
        });
    }

    // ---- Auto-start (Windows Task Scheduler) ----
    static void MapTaskEndpoints(WebApplication app)
    {
        app.MapGet("/api/auto-start", () =>
        {
            try
            {
                // 快速路径：先读本地缓存，立即返回
                var (cachedEnabled, _) = ReadOpts();

                // 后台异步校验：查计划任务，不一致则修正缓存
                // 全限定：Task 在 TaskScheduler 命名空间下有同名类型
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // 等 2 秒再查，避免安装后 Task Scheduler 尚未注册完毕导致误判
                        Thread.Sleep(2000);
                        using var ts = new TaskService();
                        var actual = ts.RootFolder.AllTasks.Any(t => t.Name == TaskName);
                        // 二次确认：若缓存为 true 但首次未找到，再等 2 秒重试
                        if (!actual && cachedEnabled)
                        {
                            Thread.Sleep(2000);
                            actual = ts.RootFolder.AllTasks.Any(t => t.Name == TaskName);
                        }
                        var (curEnabled, min) = ReadOpts();
                        if (actual != curEnabled)
                            WriteOpts(actual, min);
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
                    var (_, minimized) = ReadOpts();

                    var td = ts.NewTask();
                    td.RegistrationInfo.Description = "Douzhanzhe Console 开机自启";
                    td.Principal.RunLevel = TaskRunLevel.Highest;
                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.StopIfGoingOnBatteries = false;
                    td.Settings.DisallowStartOnRemoteAppSession = false;
                    td.Triggers.Add(new LogonTrigger());
                    td.Actions.Add(shellExe, minimized ? "--minimized" : "");
                    ts.RootFolder.RegisterTaskDefinition(TaskName, td);
                }
                else
                {
                    if (ts.RootFolder.AllTasks.Any(t => t.Name == TaskName))
                        ts.RootFolder.DeleteTask(TaskName);
                }

                // 同步写入本地缓存
                var (_, min) = ReadOpts();
                WriteOpts(enabled, min);

                return Results.Json(new { ok = true, enabled });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/auto-start"); }
        });
    }

    /// <summary>读取本地缓存的 auto-start 状态（快速路径，无 COM 开销）。</summary>
    static (bool Enabled, bool Minimized) ReadOpts()
    {
        try
        {
            if (File.Exists(OptsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(OptsPath));
                var root = doc.RootElement;
                var en = root.TryGetProperty("enabled", out var ev) && ev.ValueKind == JsonValueKind.True;
                var min = root.TryGetProperty("minimized", out var mv) && mv.ValueKind == JsonValueKind.True;
                return (en, min);
            }
        }
        catch { }
        return (false, false);
    }

    static void WriteOpts(bool enabled, bool minimized)
    {
        File.WriteAllText(OptsPath, JsonSerializer.Serialize(new { enabled, minimized }));
    }
}
