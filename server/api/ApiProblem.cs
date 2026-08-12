using Douzhanzhe.HAL;

namespace Douzhanzhe.API;

/// <summary>
/// 统一的失败响应。
///
/// 异常原文常带有本机绝对路径、驱动名与内部实现细节，直接回传给客户端既泄露信息
/// 又对用户毫无意义；同时此前所有失败都以 HTTP 200 返回，调用方无法凭状态码判断
/// 成败。这里把完整异常留在日志，客户端只拿到一个可用于反馈定位的短编号。
/// </summary>
public static class ApiProblem
{
    public static IResult From(
        Exception ex,
        string operation,
        int statusCode = StatusCodes.Status500InternalServerError,
        string? message = null)
    {
        var errorId = Guid.NewGuid().ToString("N")[..8];
        AppLog.Write("API",
            $"[{operation}] 失败 (errorId={errorId}): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

        return Results.Json(new
        {
            ok = false,
            error = message ?? DefaultMessage(statusCode),
            errorId,
        }, statusCode: statusCode);
    }

    public static IResult BadRequest(Exception ex, string operation) =>
        From(ex, operation, StatusCodes.Status400BadRequest);

    static string DefaultMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "请求参数无效",
        StatusCodes.Status403Forbidden => "该操作已被禁用",
        _ => "操作失败，请导出日志并反馈错误编号",
    };
}
