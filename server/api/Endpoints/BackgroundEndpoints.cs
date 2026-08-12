using System.Text.Json;

namespace Douzhanzhe.API.Endpoints;

/// <summary>自定义界面背景图的上传、读取、删除与遮罩/透明度选项。</summary>
public static class BackgroundEndpoints
{
    public static void MapBackgroundEndpoints(this WebApplication app)
    {
        MapOptionEndpoints(app);
        MapImageEndpoints(app);
    }

    static void MapOptionEndpoints(WebApplication app)
    {
        app.MapGet("/api/background-opts", (ConfigStore config) =>
        {
            try
            {
                var optsPath = OptsPath(config);
                if (File.Exists(optsPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(optsPath));
                    var root = doc.RootElement;
                    var enabled = root.TryGetProperty("enabled", out var ev) && ev.ValueKind == JsonValueKind.True;
                    var opacity = root.TryGetProperty("opacity", out var ov) ? Math.Clamp(ov.GetInt32(), 0, 100) : 50;
                    var maskColor = root.TryGetProperty("maskColor", out var mv) && mv.GetString() == "white" ? "white" : "black";
                    var hasImage = config.BackgroundImageFiles().Length > 0;
                    return Results.Json(new { enabled, opacity, maskColor, hasImage });
                }
                return Results.Json(new { enabled = false, opacity = 50, maskColor = "black", hasImage = false });
            }
            catch { return Results.Json(new { enabled = false, opacity = 50, maskColor = "black", hasImage = false }); }
        });

        app.MapPost("/api/background-opts", async (HttpContext ctx, ConfigStore config) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await reader.ReadToEndAsync());
                if (body == null) return Results.Json(new { ok = false, error = "无效请求" });

                // 读取当前配置
                var (enabled, opacity, maskColor) = ReadOpts(config);

                if (body.TryGetValue("enabled", out var ev)) enabled = ev.ValueKind == JsonValueKind.True;
                if (body.TryGetValue("opacity", out var ov)) opacity = Math.Clamp(ov.GetInt32(), 0, 100);
                if (body.TryGetValue("maskColor", out var mv)) maskColor = mv.GetString() == "white" ? "white" : "black";

                File.WriteAllText(OptsPath(config), JsonSerializer.Serialize(new { enabled, opacity, maskColor }));
                return Results.Json(new { ok = true, enabled, opacity, maskColor });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/background-opts"); }
        });
    }

    static void MapImageEndpoints(WebApplication app)
    {
        app.MapPost("/api/background", async (HttpContext ctx, ConfigStore config) =>
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
                foreach (var old in config.BackgroundImageFiles())
                {
                    try { File.Delete(old); }
                    catch { /* 忽略被占用的文件，写入时会被覆盖 */ }
                }

                var filePath = Path.Combine(config.ConfigDir, $"background.{ext}");
                var tmpPath = filePath + ".tmp";
                await File.WriteAllBytesAsync(tmpPath, Convert.FromBase64String(b64));
                // 原子替换：先写临时文件，再重命名
                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(tmpPath, filePath);
                return Results.Json(new { ok = true, ext });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/background"); }
        });

        app.MapGet("/api/background", async (HttpContext ctx, ConfigStore config) =>
        {
            try
            {
                var files = config.BackgroundImageFiles();
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

        app.MapDelete("/api/background", (ConfigStore config) =>
        {
            try
            {
                foreach (var f in config.BackgroundImageFiles())
                    File.Delete(f);
                // 同时禁用
                var (_, opacity, maskColor) = ReadOpts(config);
                File.WriteAllText(OptsPath(config), JsonSerializer.Serialize(new { enabled = false, opacity, maskColor }));
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/background"); }
        });
    }

    static string OptsPath(ConfigStore config) => Path.Combine(config.ConfigDir, "background-opts.json");

    static (bool Enabled, int Opacity, string MaskColor) ReadOpts(ConfigStore config)
    {
        var optsPath = OptsPath(config);
        if (!File.Exists(optsPath)) return (false, 50, "black");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(optsPath));
            var root = doc.RootElement;
            return (
                root.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True,
                root.TryGetProperty("opacity", out var o) ? o.GetInt32() : 50,
                root.TryGetProperty("maskColor", out var m) && m.GetString() == "white" ? "white" : "black");
        }
        catch { return (false, 50, "black"); }
    }
}
