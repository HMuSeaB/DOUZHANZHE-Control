using System.Text.Json;
using Douzhanzhe.API;
using Xunit;

namespace Douzhanzhe.Api.Tests;

/// <summary>
/// fork 升级路径回归: memory-fix.1 以裸名(silent/office/beast/gaming)种内置档,
/// v2.0.1 改用 cfg- 前缀。EnsureInitialized 必须一次性回收裸名内置档,
/// 否则配置栏渲染重复(4 旧 + 4 新)、当前模式仍指向裸名档导致参数分叉。
/// </summary>
public class ProfileServiceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dzz-tests", Guid.NewGuid().ToString("N"));

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    public ProfileServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    string IndexPath => Path.Combine(_dir, "profiles", ".index.json");
    string ProfileFile(string id) => Path.Combine(_dir, "profiles", $"{id}.json");

    void SeedIndex(params (string Id, bool BuiltIn)[] profiles)
    {
        var index = new
        {
            profiles = profiles.Select(p => new
            {
                id = p.Id,
                name = p.Id,
                builtIn = p.BuiltIn,
                thermalMode = p.Id.Replace("cfg-", ""),
                createdAt = (string?)null,
            }),
        };
        Directory.CreateDirectory(Path.Combine(_dir, "profiles"));
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOpts));
    }

    static PerformanceOverrides OverridesWith(int coreLimit) =>
        new() { Cpu = new CpuOverrides { CoreLimitPercent = coreLimit } };

    static PerformanceOverrides ReadOverrides(string file) =>
        JsonSerializer.Deserialize<PerformanceOverrides>(File.ReadAllText(file), JsonOpts)!;

    [Fact]
    public void 裸名内置档被回收且参数并入cfg档()
    {
        SeedIndex(("office", true), ("cfg-office", true));
        File.WriteAllText(ProfileFile("office"),
            JsonSerializer.Serialize(OverridesWith(80), JsonOpts));
        File.WriteAllText(ProfileFile("cfg-office"),
            JsonSerializer.Serialize(new PerformanceOverrides(), JsonOpts));

        var svc = new ProfileService(_dir);
        svc.EnsureInitialized(_dir);

        Assert.DoesNotContain(svc.GetAll(), p => p.Id == "office");
        Assert.Contains(svc.GetAll(), p => p.Id == "cfg-office");
        Assert.False(File.Exists(ProfileFile("office")));
        Assert.Equal(80, ReadOverrides(ProfileFile("cfg-office")).Cpu.CoreLimitPercent);
    }

    [Fact]
    public void cfg侧已定制时不覆盖裸名参数()
    {
        SeedIndex(("office", true), ("cfg-office", true));
        File.WriteAllText(ProfileFile("office"),
            JsonSerializer.Serialize(OverridesWith(80), JsonOpts));
        File.WriteAllText(ProfileFile("cfg-office"),
            JsonSerializer.Serialize(OverridesWith(50), JsonOpts));

        var svc = new ProfileService(_dir);
        svc.EnsureInitialized(_dir);

        Assert.DoesNotContain(svc.GetAll(), p => p.Id == "office");
        Assert.Equal(50, ReadOverrides(ProfileFile("cfg-office")).Cpu.CoreLimitPercent);
    }

    [Fact]
    public void 裸名用户档不被回收()
    {
        SeedIndex(("cfg-office", true), ("office", false));
        File.WriteAllText(ProfileFile("cfg-office"),
            JsonSerializer.Serialize(new PerformanceOverrides(), JsonOpts));
        File.WriteAllText(ProfileFile("office"),
            JsonSerializer.Serialize(OverridesWith(80), JsonOpts));

        var svc = new ProfileService(_dir);
        svc.EnsureInitialized(_dir);

        Assert.Contains(svc.GetAll(), p => p.Id == "office");
        Assert.True(File.Exists(ProfileFile("office")));
    }

    [Fact]
    public void 全新安装正常种四个cfg内置档()
    {
        var svc = new ProfileService(_dir);
        svc.EnsureInitialized(_dir);

        var ids = svc.GetAll().Select(p => p.Id).ToHashSet();
        Assert.Equal(new HashSet<string> { "cfg-silent", "cfg-office", "cfg-beast", "cfg-gaming" }, ids);
    }
}
