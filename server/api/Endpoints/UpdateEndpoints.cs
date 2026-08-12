using System.Text.Json;
using System.Text.RegularExpressions;
using Douzhanzhe.HAL;

namespace Douzhanzhe.API.Endpoints;

/// <summary>对 GitHub Releases 做版本比对，供前端提示升级。</summary>
public static class UpdateEndpoints
{
    const string LatestReleaseUrl =
        "https://api.github.com/repos/KanzakiK/DOUZHANZHE-Control/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DouzhanzheConsole-UpdateChecker/1.0");
        return client;
    }

    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var currentVersion = DetectAppVersion();

        app.MapGet("/api/update/check", async () =>
        {
            try
            {
                var res = await Http.GetAsync(LatestReleaseUrl);

                // 无 release (404) 或网络故障 → 视为无更新
                if (!res.IsSuccessStatusCode)
                    return Results.Json(new { available = false, currentVersion, reason = "无法获取发布信息" });

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
                    Version.TryParse(currentVersion, out var current))
                {
                    isNewer = latest > current;
                }

                return Results.Json(new
                {
                    available = isNewer,
                    currentVersion,
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
                return Results.Json(new { available = false, currentVersion, error = "检查更新失败，请稍后重试" });
            }
        });
    }

    /// <summary>
    /// 从前端 JS bundle 提取版本号（构建时 SettingsPanel.jsx 中的 "Douzhanzhe Console vX.Y.Z"）。
    /// 覆盖安装时 wwwroot/assets 可能残留多个旧 bundle，必须遍历所有文件取最大版本号。
    /// </summary>
    static string DetectAppVersion()
    {
        try
        {
            var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets");
            if (!Directory.Exists(wwwroot)) return "0.0.0";

            var maxVer = new Version(0, 0, 0);
            foreach (var jsFile in Directory.GetFiles(wwwroot, "index-*.js"))
            {
                try
                {
                    var jsContent = File.ReadAllText(jsFile);
                    var m = Regex.Match(jsContent, @"Douzhanzhe Console v(\d+\.\d+\.\d+)");
                    if (m.Success && Version.TryParse(m.Groups[1].Value, out var v) && v > maxVer)
                        maxVer = v;
                }
                catch { /* 单个文件读取失败不影响其他 */ }
            }
            return maxVer > new Version(0, 0, 0) ? maxVer.ToString() : "0.0.0";
        }
        catch { return "0.0.0"; }
    }
}
