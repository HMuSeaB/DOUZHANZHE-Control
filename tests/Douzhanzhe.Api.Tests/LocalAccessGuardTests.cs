using Douzhanzhe.API;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Douzhanzhe.Api.Tests;

/// <summary>
/// 鍚屾簮瀹堝崼鏄敮涓€鎸″湪瑁哥‖浠剁鐐瑰墠闈㈢殑闃茬嚎锛屼换浣曟斁瀹介兘鍙兘璁╂祻瑙堝櫒閲岀殑绗笁鏂归〉闈?/// 鏀瑰啓鍔熻€楀鎴栫洿鍐?EC锛屽洜姝よ繖閲岄€愭潯閽夋鏀捐涓庢嫆缁濈殑杈圭晫銆?/// </summary>
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

    // 鈹€鈹€ 娴忚鍣ㄥ悓婧愯姹傦細鍓嶇涓?API 鐢卞悓涓€涓?Kestrel 鎻愪緵锛屽繀椤绘斁琛?鈹€鈹€

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

    // 鈹€鈹€ 璺ㄧ珯璇锋眰锛氭伓鎰忛〉闈㈤€氳繃鏈湴绔彛鎿嶄綔纭欢鐨勪富瑕佽矾寰?鈹€鈹€

    [Fact]
    public void Denies_cross_site_fetch_from_external_page()
    {
        Assert.False(Allowed(Request(secFetchSite: "cross-site", origin: "http://evil.example")));
    }

    [Fact]
    public void Denies_cross_site_request_without_origin()
    {
        // 褰㈠ <img src> / <script src> 鐨勭畝鍗曡法绔?GET 涓嶅甫 Origin锛屽彧鑳介潬 Sec-Fetch-Site 璇嗗埆
        Assert.False(Allowed(Request(secFetchSite: "cross-site")));
    }

    [Fact]
    public void Denies_same_site_but_not_same_origin()
    {
        Assert.False(Allowed(Request(secFetchSite: "same-site", origin: "http://sub.localhost:9000")));
    }

    // 鈹€鈹€ 涓嶅彂閫?Sec-Fetch-Site 鐨勬棫娴忚鍣細閫€鍥?Origin 鐧藉悕鍗?鈹€鈹€

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

    // 鈹€鈹€ DNS rebinding锛氬閮ㄥ煙鍚嶈В鏋愬埌 127.0.0.1 鍚庯紝Host 澶翠細鏆撮湶鐪熷疄鏉ユ簮 鈹€鈹€

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

    // 鈹€鈹€ 闈炴祻瑙堝櫒瀹㈡埛绔細鏃犳潵婧愭爣璁版椂蹇呴』鍑虹ず浼氳瘽浠ょ墝 鈹€鈹€

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
        // WebSocket 鎻℃墜鏃犳硶鎼哄甫鑷畾涔夎姹傚ご锛屽彧鑳借蛋鏌ヨ涓?        Assert.True(Allowed(Request(tokenQuery: Token)));
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

    // 鈹€鈹€ 寮€鍙戞ā寮忥細Vite 椤甸潰 (:5173) 鐩磋繛鍚庣 WebSocket 灞炰簬璺ㄧ珯锛岄渶鎸夋潵婧愭斁琛?鈹€鈹€

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

    // 鈹€鈹€ 瑁哥‖浠剁鐐归粯璁ゅ叧闂?鈹€鈹€

    [Fact]
    public void Unsafe_tools_are_disabled_unless_explicitly_unlocked()
    {
        Assert.False(LocalAccessGuard.UnsafeToolsEnabled);
        Assert.NotNull(LocalAccessGuard.BlockUnsafeTool("/api/smu/raw"));
    }
}
