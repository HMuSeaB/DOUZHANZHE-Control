using Douzhanzhe.HAL;

namespace Douzhanzhe.API;

/// <summary>
/// 统一的失败响应：完整异常写入日志，客户端只收到短 errorId。
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
