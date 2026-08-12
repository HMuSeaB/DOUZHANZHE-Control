using System.Text;
using System.Text.Json;
using Douzhanzhe.API;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Douzhanzhe.Api.Tests;

/// <summary>
/// 异常原文常包含本机绝对路径与驱动名，一旦回传给客户端就会随截图或反馈外泄。
/// 这里钉死"细节只进日志、客户端只拿编号"这条边界。
/// </summary>
public class ApiProblemTests
{
    const string SecretDetail = @"D:\Program Files\Secret\driver.dll 拒绝访问";

    // JsonHttpResult 会从 RequestServices 解析日志与 JSON 选项，脱离主机运行时需自备容器
    static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddOptions()
        .BuildServiceProvider();

    static async Task<(int StatusCode, string Body)> Execute(IResult result)
    {
        var ctx = new DefaultHttpContext { RequestServices = Services };
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        await result.ExecuteAsync(ctx);

        buffer.Position = 0;
        return (ctx.Response.StatusCode, Encoding.UTF8.GetString(buffer.ToArray()));
    }

    [Fact]
    public async Task Does_not_leak_exception_text_to_client()
    {
        var (_, body) = await Execute(ApiProblem.From(new InvalidOperationException(SecretDetail), "/api/test"));

        Assert.DoesNotContain("Secret", body);
        Assert.DoesNotContain("driver.dll", body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
    }

    [Fact]
    public async Task Does_not_leak_stack_trace()
    {
        Exception captured;
        try { throw new InvalidOperationException(SecretDetail); }
        catch (Exception ex) { captured = ex; }

        var (_, body) = await Execute(ApiProblem.From(captured, "/api/test"));

        Assert.DoesNotContain("at Douzhanzhe", body);
        Assert.DoesNotContain("StackTrace", body);
    }

    [Fact]
    public async Task Reports_server_error_status_by_default()
    {
        var (status, _) = await Execute(ApiProblem.From(new Exception("boom"), "/api/test"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }

    [Fact]
    public async Task Reports_bad_request_status_for_invalid_input()
    {
        var (status, body) = await Execute(ApiProblem.BadRequest(new FormatException("bad hex"), "/api/test"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.DoesNotContain("bad hex", body);
    }

    [Fact]
    public async Task Keeps_ok_false_envelope_for_existing_frontend_callers()
    {
        var (_, body) = await Execute(ApiProblem.From(new Exception("boom"), "/api/test"));

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Supplies_a_correlation_id_for_log_lookup()
    {
        var (_, body) = await Execute(ApiProblem.From(new Exception("boom"), "/api/test"));

        using var doc = JsonDocument.Parse(body);
        var errorId = doc.RootElement.GetProperty("errorId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(errorId));
    }

    [Fact]
    public async Task Uses_a_distinct_id_per_failure()
    {
        var (_, first) = await Execute(ApiProblem.From(new Exception("boom"), "/api/test"));
        var (_, second) = await Execute(ApiProblem.From(new Exception("boom"), "/api/test"));

        using var a = JsonDocument.Parse(first);
        using var b = JsonDocument.Parse(second);
        Assert.NotEqual(
            a.RootElement.GetProperty("errorId").GetString(),
            b.RootElement.GetProperty("errorId").GetString());
    }
}
