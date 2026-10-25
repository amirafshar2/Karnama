using Karnama.Api.Infrastructure;
using Karnama.Application.Reports;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N8: period work report and worker scores. Who sees whom is decided in <see cref="PerformanceService"/>.</summary>
internal static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        g.MapGet("/work", async (DateOnly from, DateOnly to, Guid? user, PerformanceService svc, HttpContext http, CancellationToken ct) => (await svc.WorkAsync(from, to, user, ct)).ToHttp(http));
        g.MapGet("/work/{userId:guid}", async (Guid userId, DateOnly from, DateOnly to, PerformanceService svc, HttpContext http, CancellationToken ct) =>
            (await svc.DetailsAsync(userId, from, to, ct)).ToHttp(http));
        g.MapGet("/work/export", async (DateOnly from, DateOnly to, PerformanceService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.ExcelAsync(from, to, ct);
            return r.IsFailure ? r.ToHttp(http) : Results.File(r.Value!, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"work-report-{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
        }).RequirePermission(Permissions.ReportsExport, PermissionScope.All);
        g.MapGet("/scores", async (DateOnly from, DateOnly to, PerformanceService svc, HttpContext http, CancellationToken ct) => (await svc.ScoresAsync(from, to, ct)).ToHttp(http));
        g.MapGet("/scores/{userId:guid}/trend", async (Guid userId, PerformanceService svc, HttpContext http, CancellationToken ct) => (await svc.TrendAsync(userId, ct)).ToHttp(http));
        g.MapGet("/scores/settings", async (PerformanceService svc, CancellationToken ct) => Results.Ok(await svc.SettingsAsync(ct)))
            .RequirePermission(Permissions.ScoresConfigure, PermissionScope.All);
        g.MapPut("/scores/settings", async (ScoreSettingsDto body, PerformanceService svc, HttpContext http, CancellationToken ct) => (await svc.SaveSettingsAsync(body, ct)).ToHttp(http))
            .RequirePermission(Permissions.ScoresConfigure, PermissionScope.All);
    }
}
