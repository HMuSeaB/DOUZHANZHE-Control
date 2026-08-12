using Douzhanzhe.API;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Douzhanzhe.Api.Tests;

/// <summary>
/// 同源守卫是唯一挡在裸硬件端点前面的防线，任何放宽都可能让浏览器里的第三方页面
/// 改写功耗墙或直写 EC，因此这里逐条钉死放行与拒绝的边界。
/// </summary>
public class LocalAccessGuardTests
{
    const int ServerPort = 3100;

    static readonly string Token = LoadToken();

    static string LoadToken()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dzz-tests", Guid.NewGuid().ToString("N"));
        LocalAccessGuard.InitToken(dir);
        return File.ReadAllText(LocalAccessGuard.TokenPath);
    }

    static HttpContext Request(
        string? secFetchSite = null,
        string? origin = null,
        string? host = "127.0.0.1",
        string? tokenHeader = null,
        string? tokenQuery = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host ?? "127.0.0.1", ServerPort);
        ctx.Connection.LocalPort = ServerPort;
        if (secFetchSite is not null) ctx.Request.Headers["Sec-Fetch-Site"] = secFetchSite;
        if (origin is not null) ctx.Request.Headers.Origin = origin;
        if (tokenHeader is not null) ctx.Request.Headers[LocalAccessGuard.TokenHeader] = tokenHeader;
        if (tokenQuery is not null) ctx.Request.QueryString = new QueryString("?token=" + Uri.EscapeDataString(tokenQuery));
        return ctx;
    }

    static bool Allowed(HttpContext ctx, bool devMode = false) =>
        LocalAccessGuard.IsAllowed(ctx, devMode, out _);

    // ── 浏览器同源请求：前端与 API 由同一个 Kestrel 提供，必须放行 ──

    [Theory]
    [InlineData("same-origin")]
    [InlineData("none")]
    public void Allows_same_origin_and_direct_navigation(string secFetchSite)
    {
        Assert.True(Allowed(Request(secFetchSite: secFetchSite)));
    }

    [Fact]
    public void Allows_ipv6_loopback_host()
    {
        Assert.True(Allowed(Request(secFetchSite: "same-origin", host: "[::1]")));
    }

    // ── 跨站请求：恶意页面通过本地端口操作硬件的主要路径 ──

    [Fact]
    public void Denies_cross_site_fetch_from_external_page()
    {
        Assert.False(Allowed(Request(secFetchSite: "cross-site", origin: "http://evil.example")));
    }

    [Fact]
    public void Denies_cross_site_request_without_origin()
    {
        // 形如 <img src> / <script src> 的简单跨站 GET 不带 Origin，只能靠 Sec-Fetch-Site 识别
        Assert.False(Allowed(Request(secFetchSite: "cross-site")));
    }

    [Fact]
    public void Denies_same_site_but_not_same_origin()
    {
        Assert.False(Allowed(Request(secFetchSite: "same-site", origin: "http://sub.localhost:9000")));
    }

    // ── 不发送 Sec-Fetch-Site 的旧浏览器：退回 Origin 白名单 ──

    [Fact]
    public void Denies_legacy_cross_site_form_post()
    {
        Assert.False(Allowed(Request(origin: "http://evil.example")));
    }

    [Theory]
    [InlineData("http://127.0.0.1:3100")]
    [InlineData("http://localhost:3100")]
    public void Allows_legacy_same_origin(string origin)
    {
        Assert.True(Allowed(Request(origin: origin)));
    }

    [Fact]
    public void Denies_other_loopback_port_in_production()
    {
        Assert.False(Allowed(Request(origin: "http://127.0.0.1:5173")));
    }

    // ── DNS rebinding：外部域名解析到 127.0.0.1 后，Host 头会暴露真实来源 ──

    [Fact]
    public void Denies_non_loopback_host()
    {
        Assert.False(Allowed(Request(secFetchSite: "same-origin", host: "attacker.com")));
    }

    [Fact]
    public void Denies_non_loopback_host_even_in_dev_mode()
    {
        Assert.False(Allowed(Request(secFetchSite: "same-origin", host: "attacker.com"), devMode: true));
    }

    // ── 非浏览器客户端：无来源标记时必须出示会话令牌 ──

    [Fact]
    public void Denies_bare_request_without_token()
    {
        Assert.False(Allowed(Request()));
    }

    [Fact]
    public void Allows_bare_request_with_valid_token_header()
    {
        Assert.True(Allowed(Request(tokenHeader: Token)));
    }

    [Fact]
    public void Allows_valid_token_via_query_for_websocket()
    {
        // WebSocket 握手无法携带自定义请求头，只能走查询串
        Assert.True(Allowed(Request(tokenQuery: Token)));
    }

    [Fact]
    public void Denies_wrong_token()
    {
        Assert.False(Allowed(Request(tokenHeader: "wrong-token-value")));
    }

    [Fact]
    public void Denies_token_of_matching_length_but_different_content()
    {
        Assert.False(Allowed(Request(tokenHeader: new string('A', Token.Length))));
    }

    // ── 开发模式：Vite 页面 (:5173) 直连后端 WebSocket 属于跨站，需按来源放行 ──

    [Fact]
    public void Allows_vite_dev_origin_in_dev_mode()
    {
        Assert.True(Allowed(Request(secFetchSite: "cross-site", origin: "http://localhost:5173"), devMode: true));
    }

    [Fact]
    public void Denies_external_origin_even_in_dev_mode()
    {
        Assert.False(Allowed(Request(secFetchSite: "cross-site", origin: "http://evil.example"), devMode: true));
    }

    // ── 裸硬件端点默认关闭 ──

    [Fact]
    public void Unsafe_tools_are_disabled_unless_explicitly_unlocked()
    {
        Assert.False(LocalAccessGuard.UnsafeToolsEnabled);
        Assert.NotNull(LocalAccessGuard.BlockUnsafeTool("/api/smu/raw"));
    }
}
