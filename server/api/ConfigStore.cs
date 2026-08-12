using System.Text.Json;

namespace Douzhanzhe.API;

/// <summary>
/// 配置目录下的 JSON 读写与性能覆盖项持久化。
///
/// 这些能力原本是 Program.cs 顶层语句里的局部函数，被所有端点以闭包方式共享，
/// 导致端点无法从 Program.cs 中拆出。提取为可注入的单例后，端点只需声明依赖。
/// </summary>
public sealed class ConfigStore
{
    static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    const string PerfFile = "performance-overrides.json";

    readonly object _perfLock = new();

    public ConfigStore(string configDir)
    {
        ConfigDir = configDir;
        Directory.CreateDirectory(configDir);
    }

    public string ConfigDir { get; }

    public T Read<T>(string fileName, T fallback) where T : class
    {
        var filePath = Path.Combine(ConfigDir, fileName);
        if (!File.Exists(filePath)) return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), ReadOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>先写临时文件再改名，避免断电或崩溃留下半截 JSON。</summary>
    public void Write<T>(string fileName, T data)
    {
        var filePath = Path.Combine(ConfigDir, fileName);
        var tmpPath = filePath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(data, WriteOptions));
        File.Move(tmpPath, filePath, overwrite: true);
    }

    public PerformanceOverrides LoadPerfOverrides() => Read(PerfFile, new PerformanceOverrides());

    public void SavePerfOverrides(Action<PerformanceOverrides> mutate)
    {
        lock (_perfLock)
        {
            var o = LoadPerfOverrides();
            mutate(o);
            Write(PerfFile, o);
        }
    }

    /// <summary>只取图片本体，排除同前缀的 background-opts.json 与写入中的 .tmp。</summary>
    public string[] BackgroundImageFiles() =>
        Directory.GetFiles(ConfigDir, "background.*")
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
