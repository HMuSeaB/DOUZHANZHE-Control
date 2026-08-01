using System.Text.Json;
using Douzhanzhe.HAL;

namespace Douzhanzhe.API;

public class BackgroundRotationService : BackgroundService
{
    private sealed class BackgroundOpts
    {
        public bool Enabled { get; set; }
        public string Source { get; set; } = "local";
        public string Interval { get; set; } = "1h";
        public string ApiUrl { get; set; } = "";
    }

    private readonly HttpClient _http = new();
    private readonly string _configDir;
    private readonly string _optsPath;
    private DateTime _lastRotation = DateTime.MinValue;
    private string _configSignature = "";
    private int _urlIndex;

    public BackgroundRotationService(string configDir)
    {
        _configDir = configDir;
        _optsPath = Path.Combine(configDir, "background-opts.json");
        _http.Timeout = TimeSpan.FromSeconds(12);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DouzhanzheConsole-BackgroundRotation/1.0");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 等 API 完全启动后再做首次轮换
        await Task.Delay(3000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var opts = ReadOpts();
                var signature = $"{opts.Enabled}|{opts.Source}|{opts.Interval}|{opts.ApiUrl}";
                var intervalSeconds = ParseInterval(opts.Interval);
                var now = DateTime.UtcNow;

                if (opts.Enabled && opts.Source == "network" && !string.IsNullOrWhiteSpace(opts.ApiUrl))
                {
                    var due = now - _lastRotation >= TimeSpan.FromSeconds(intervalSeconds);
                    if (signature != _configSignature || due)
                    {
                        await RotateOnceAsync(opts.ApiUrl, ct);
                        _lastRotation = DateTime.UtcNow;
                    }
                }

                _configSignature = signature;
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Write("Background", $"轮换服务异常: {ex.Message}");
                try { await Task.Delay(10_000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private BackgroundOpts ReadOpts()
    {
        var opts = new BackgroundOpts();
        if (!File.Exists(_optsPath)) return opts;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_optsPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True)
                opts.Enabled = true;
            if (root.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String)
                opts.Source = s.GetString() ?? "local";
            if (root.TryGetProperty("interval", out var i) && i.ValueKind == JsonValueKind.String)
                opts.Interval = i.GetString() ?? "1h";
            if (root.TryGetProperty("apiUrl", out var u) && u.ValueKind == JsonValueKind.String)
                opts.ApiUrl = u.GetString() ?? "";
        }
        catch { /* 配置损坏时按默认处理 */ }
        return opts;
    }

    private static int ParseInterval(string interval)
    {
        if (string.IsNullOrWhiteSpace(interval)) return 3600;
        var s = interval.Trim().ToLowerInvariant();
        return s switch
        {
            "30m" => 1800,
            "3h" => 10800,
            "1d" => 86400,
            _ when s.EndsWith("h") && int.TryParse(s[..^1], out var h) => h * 3600,
            _ when s.EndsWith("m") && int.TryParse(s[..^1], out var m) => m * 60,
            _ when s.EndsWith("s") && int.TryParse(s[..^1], out var sec) => Math.Max(10, sec),
            _ => 3600, // 默认 1h
        };
    }

    private async Task RotateOnceAsync(string apiUrl, CancellationToken ct)
    {
        try
        {
            var urls = await FetchImageUrlsAsync(apiUrl, ct);
            if (urls.Count == 0)
            {
                AppLog.Write("Background", "网络轮换: API 返回空列表，保留当前图");
                return;
            }

            var pick = urls[_urlIndex % urls.Count];
            _urlIndex++;
            AppLog.Write("Background", $"网络轮换: 下载 {pick}");

            using var resp = await _http.GetAsync(pick, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > 20 * 1024 * 1024)
            {
                AppLog.Write("Background", $"网络轮换: 图片大小非法 ({bytes.Length} bytes)，保留当前图");
                return;
            }

            var ext = DetectExt(resp, pick);
            await ReplaceImageAsync(bytes, ext, pick, ct);
        }
        catch (Exception ex)
        {
            AppLog.Write("Background", $"网络轮换失败: {ex.Message}，保留当前图");
        }
    }

    private async Task<List<string>> FetchImageUrlsAsync(string apiUrl, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(apiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var urls = new List<string>();

        void Collect(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray()) Collect(item);
            }
            else if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s) && IsHttpUrl(s)) urls.Add(s);
            }
            else if (el.ValueKind == JsonValueKind.Object
                     && el.TryGetProperty("url", out var u)
                     && u.ValueKind == JsonValueKind.String)
            {
                var s = u.GetString();
                if (!string.IsNullOrWhiteSpace(s) && IsHttpUrl(s)) urls.Add(s);
            }
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("urls", out var arr))
            Collect(arr);
        else
            Collect(root);

        return urls;
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string DetectExt(HttpResponseMessage resp, string pick)
    {
        var media = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        if (media.Contains("png")) return ".png";
        if (media.Contains("jpeg") || media.Contains("jpg")) return ".jpg";
        if (media.Contains("webp")) return ".webp";

        var path = Uri.TryCreate(pick, UriKind.Absolute, out var u) ? u.AbsolutePath.ToLowerInvariant() : "";
        if (path.EndsWith(".png")) return ".png";
        if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) return ".jpg";
        if (path.EndsWith(".webp")) return ".webp";
        return ".png";
    }

    private async Task ReplaceImageAsync(byte[] bytes, string ext, string sourceUrl, CancellationToken ct)
    {
        var filePath = Path.Combine(_configDir, "background" + ext);
        var tmpPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, bytes, ct);

        try
        {
            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Write("Background", $"网络轮换: 替换图片失败 {ex.Message}，保留当前图");
            try { File.Delete(tmpPath); } catch { }
            return;
        }

        // 清理其它扩展名的旧图，避免 /api/background 取到过期文件
        foreach (var old in Directory.GetFiles(_configDir, "background.*"))
        {
            if (old.Equals(filePath, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsImageFile(old))
            {
                try { File.Delete(old); } catch { }
            }
        }

        AppLog.Write("Background", $"网络轮换: ✓ {Path.GetFileName(filePath)} ← {sourceUrl}");
    }

    private static bool IsImageFile(string path)
    {
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
