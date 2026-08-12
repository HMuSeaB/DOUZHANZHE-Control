using System.Text.Json;

namespace Douzhanzhe.API.Endpoints;

/// <summary>游戏配置规则的增删改查、进程监控状态、已安装游戏扫描与批量导入。</summary>
public static class GameProfileEndpoints
{
    public static void MapGameProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/game-profiles", (GameProfileService svc) =>
        {
            return Results.Json(new
            {
                enabled = svc.Enabled,
                defaultMode = svc.DefaultMode,
                profiles = svc.GetAll()
            });
        });

        app.MapPost("/api/game-profiles", (GameProfileRequest req, GameProfileService svc) =>
        {
            try
            {
                var profile = new GameProfile
                {
                    Name = req.Name ?? "",
                    ExePath = req.ExePath ?? "",
                    ExeName = req.ExeName ?? Path.GetFileName(req.ExePath ?? ""),
                    TargetMode = req.TargetMode ?? svc.DefaultMode,
                    Enabled = req.Enabled ?? true,
                    Source = req.Source ?? "manual"
                };
                var created = svc.Add(profile);
                return Results.Created($"/api/game-profiles/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblem.BadRequest(ex, "/api/game-profiles");
            }
        });

        app.MapPut("/api/game-profiles/{id}", (string id, GameProfileRequest req, GameProfileService svc) =>
        {
            try
            {
                var existing = svc.GetById(id);
                if (existing == null)
                    return Results.NotFound(new { error = "规则不存在" });

                var updated = svc.Update(id, new GameProfile
                {
                    Id = id,
                    Name = req.Name ?? existing.Name,
                    ExePath = req.ExePath ?? existing.ExePath,
                    ExeName = req.ExeName ?? existing.ExeName,
                    TargetMode = req.TargetMode ?? existing.TargetMode,
                    Enabled = req.Enabled ?? existing.Enabled,
                    Source = req.Source ?? existing.Source
                });
                return Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblem.BadRequest(ex, "/api/game-profiles/{id}");
            }
        });

        app.MapDelete("/api/game-profiles/{id}", (string id, GameProfileService svc) =>
        {
            svc.Delete(id);
            return Results.Ok();
        });

        app.MapPut("/api/game-profiles/config", (GameConfigRequest req, GameProfileService svc) =>
        {
            svc.UpdateConfig(req.Enabled, req.DefaultMode);
            return Results.Ok(new { enabled = svc.Enabled, defaultMode = svc.DefaultMode });
        });

        app.MapGet("/api/game-profiles/status", (ProcessMonitorService svc) =>
        {
            return Results.Json(svc.GetStatus());
        });

        app.MapGet("/api/game-profiles/file-pick", () =>
        {
            // 使用 Windows 文件选择对话框
            var ofd = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择游戏主程序",
                Multiselect = false
            };

            // 需要在 STA 线程上运行
            string? result = null;
            var thread = new Thread(() =>
            {
                if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    result = ofd.FileName;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (result == null)
                return Results.Ok(new { selected = false, path = (string?)null, name = (string?)null });

            var fileName = Path.GetFileNameWithoutExtension(result);
            return Results.Ok(new { selected = true, path = result, name = fileName });
        });

        // 扫描已安装游戏（Steam + Epic）
        app.MapGet("/api/game-profiles/scan", (GameProfileService profiles) =>
        {
            var results = GameScannerService.Scan(profiles);
            return Results.Json(results);
        });

        // 批量添加游戏
        app.MapPost("/api/game-profiles/batch", async (HttpRequest req, GameProfileService profiles) =>
        {
            var body = await req.ReadFromJsonAsync<JsonElement>();
            if (!body.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
                return Results.BadRequest(new { error = "games array required" });

            int added = 0;
            foreach (var g in games.EnumerateArray())
            {
                var name = g.TryGetProperty("name", out var n) ? n.GetString() : null;
                var exePath = g.TryGetProperty("exePath", out var ep) ? ep.GetString() : null;
                var targetMode = g.TryGetProperty("targetMode", out var tm) ? tm.GetString() : "gaming";
                var source = g.TryGetProperty("source", out var src) ? src.GetString() : "scan";

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exePath)) continue;

                var exeName = Path.GetFileName(exePath);
                try
                {
                    profiles.Add(new GameProfile
                    {
                        Name = name,
                        ExeName = exeName,
                        ExePath = exePath,
                        TargetMode = targetMode,
                        Source = source,
                        Enabled = true
                    });
                    added++;
                }
                catch { }
            }

            return Results.Ok(new { added });
        });
    }
}
