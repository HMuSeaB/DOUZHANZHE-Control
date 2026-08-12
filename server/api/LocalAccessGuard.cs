using System.Net;
using System.Security.Cryptography;
using System.Text;
using Douzhanzhe.HAL;

namespace Douzhanzhe.API;

/// <summary>
/// 本机同源守卫。
///
/// 本 API 对外暴露裸硬件能力（SMU 命令、EC 寄存器、IO 端口、任意 WMI 方法），
/// 仅绑定 127.0.0.1 并不足以保护它：用户在同一台机器上打开的任意网页都能向
/// 本地端口发请求，从而改写功耗墙、超频或直写 EC。因此所有 /api 与 /ws 请求
/// 必须先通过来源校验。
/// </summary>
public static class LocalAccessGuard
{
    public const string TokenHeader = "X-Douzhanzhe-Token";

    static byte[] _tokenBytes = Array.Empty<byte>();

    public static string TokenPath { get; private set; } = "";

    /// <summary>
    /// 裸硬件调试端点（任意 SMU/WMI 命令、IO 端口探测、EC 扫描）默认禁用。
    /// 逆向调试时以环境变量 DZZ_UNSAFE_TOOLS=1 启动后端才会开放。
    /// </summary>
    public static bool UnsafeToolsEnabled { get; } =
        Environment.GetEnvironmentVariable("DZZ_UNSAFE_TOOLS") is "1" or "true";

    /// <summary>
    /// 每次启动生成新的会话令牌并落盘。浏览器同源请求不需要它；
    /// 它只用于放行本机命令行工具/脚本这类不带浏览器来源标记的客户端。
    /// </summary>
    public static void InitToken(string dir)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _tokenBytes = Encoding.UTF8.GetBytes(token);
        TokenPath = Path.Combine(dir, "session.token");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(TokenPath, token);
        }
        catch (Exception ex)
        {
            AppLog.Write("Guard", $"会话令牌写入失败 {TokenPath}: {ex.Message}");
        }
    }

    public static bool IsAllowed(HttpContext ctx, bool devMode, out string reason)
    {
        // 1. Host 必须是回环名/地址，阻断 DNS rebinding（外部域名解析到 127.0.0.1）。
        var host = ctx.Request.Host.Host;
        if (!IsLoopbackHost(host))
        {
            reason = $"Host 非回环: {host}";
            return false;
        }

        var origin = ctx.Request.Headers.Origin.ToString();
        var site = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
        var localPort = ctx.Connection.LocalPort;

        // 2. Sec-Fetch-Site 由浏览器强制写入，页面无法伪造，是最可靠的判据。
        if (!string.IsNullOrEmpty(site))
        {
            if (site is "same-origin" or "none")
            {
                reason = "";
                return true;
            }
            // 开发模式下 Vite 页面 (:5173) 直连后端 WebSocket 属于 cross-site，按来源放行。
            if (IsAllowedOrigin(origin, localPort, devMode))
            {
                reason = "";
                return true;
            }
            reason = $"跨站请求被拒 (Sec-Fetch-Site={site}, Origin={Describe(origin)})";
            return false;
        }

        // 3. 不发送 Sec-Fetch-Site 的旧浏览器：只认 Origin 白名单。
        if (!string.IsNullOrEmpty(origin))
        {
            if (IsAllowedOrigin(origin, localPort, devMode))
            {
                reason = "";
                return true;
            }
            reason = $"Origin 不在白名单: {origin}";
            return false;
        }

        // 4. 无任何浏览器来源标记（命令行 / 脚本）：必须携带会话令牌。
        var supplied = ctx.Request.Headers[TokenHeader].ToString();
        if (string.IsNullOrEmpty(supplied))
            supplied = ctx.Request.Query["token"].ToString();
        if (TokenMatches(supplied))
        {
            reason = "";
            return true;
        }

        reason = "缺少或错误的会话令牌";
        return false;
    }

    public static IResult? BlockUnsafeTool(string endpoint)
    {
        if (UnsafeToolsEnabled) return null;
        AppLog.Write("Guard", $"裸硬件端点被拒: {endpoint}（未设置 DZZ_UNSAFE_TOOLS）");
        return Results.Json(new
        {
            ok = false,
            error = "裸硬件调试端点默认禁用，需以环境变量 DZZ_UNSAFE_TOOLS=1 启动后端",
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    static bool TokenMatches(string supplied)
    {
        if (_tokenBytes.Length == 0 || string.IsNullOrEmpty(supplied)) return false;
        var given = Encoding.UTF8.GetBytes(supplied);
        if (given.Length != _tokenBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(given, _tokenBytes);
    }

    static bool IsAllowedOrigin(string origin, int localPort, bool devMode)
    {
        if (string.IsNullOrEmpty(origin)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (!IsLoopbackHost(uri.Host)) return false;
        // 生产环境只信任本服务自身端口；开发环境额外放行 Vite 等其它回环端口。
        return devMode || uri.Port == localPort;
    }

    static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var bare = host.Trim('[', ']');
        return IPAddress.TryParse(bare, out var ip) && IPAddress.IsLoopback(ip);
    }

    static string Describe(string origin) => string.IsNullOrEmpty(origin) ? "<无>" : origin;
}
