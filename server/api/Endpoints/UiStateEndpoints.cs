using System.Text.Json;

namespace Douzhanzhe.API.Endpoints;

/// <summary>仪表盘卡片排序与显隐的持久化，以及出厂默认布局。</summary>
public static class UiStateEndpoints
{
    static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static void MapUiStateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/ui-state", (ConfigStore config) =>
        {
            return Results.Json(config.Read<UiState>("ui-state.json", new UiState()));
        });

        app.MapPost("/api/ui-state", async (HttpContext ctx, ConfigStore config) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = JsonSerializer.Deserialize<UiState>(await reader.ReadToEndAsync(), ReadOptions);
                config.Write("ui-state.json", body ?? new UiState());
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/ui-state"); }
        });

        app.MapGet("/api/default-config", (ConfigStore config) =>
        {
            return Results.Json(config.Read<DefaultConfig>("dashboard-default.json", new DefaultConfig()));
        });

        app.MapPost("/api/default-config", async (HttpContext ctx, ConfigStore config) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = JsonSerializer.Deserialize<DefaultConfig>(await reader.ReadToEndAsync(), ReadOptions);
                config.Write("dashboard-default.json", body ?? new DefaultConfig());
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ApiProblem.From(ex, "/api/default-config"); }
        });
    }
}
