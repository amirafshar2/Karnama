using Karnama.Api.Infrastructure;
using Karnama.Application.Common;
using Karnama.Application.Crm;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N7: CRM. Visibility (own vs all leads) is enforced in <see cref="CrmService"/>; others' leads are 404.</summary>
internal static class CrmEndpoints
{
    public static void MapCrm(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/crm").WithTags("CRM").RequirePermission(Permissions.CrmLeads, PermissionScope.Own).AddEndpointFilter<ValidationFilter>();
        g.MapGet("/stages", async (CrmService svc, CancellationToken ct) => Results.Ok(await svc.StagesAsync(ct)));
        g.MapGet("/sources", async (CrmService svc, CancellationToken ct) => Results.Ok(await svc.SourcesAsync(ct)));
        g.MapGet("/marketers", async (CrmService svc, HttpContext http, CancellationToken ct) => (await svc.MarketersAsync(ct)).ToHttp(http));
        g.MapGet("/board", async (Guid? owner, string? q, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.BoardAsync(owner, q, ct)).ToHttp(http));
        g.MapGet("/leads", async (string? cursor, int? limit, string? q, Guid? owner, Guid? stage, Guid? source, CrmService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ListAsync(new KeysetQuery(cursor, limit ?? 30, q), owner, stage, source, ct)).ToHttp(http));
        g.MapGet("/today", async (CrmService svc, HttpContext http, CancellationToken ct) => (await svc.TodayAsync(ct)).ToHttp(http));
        g.MapGet("/calendar", async (DateTimeOffset from, DateTimeOffset to, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.CalendarAsync(from, to, ct)).ToHttp(http));
        g.MapGet("/report", async (DateOnly from, DateOnly to, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.ReportAsync(from, to, ct)).ToHttp(http));
        g.MapGet("/leads/{id:guid}", async (Guid id, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        g.MapPost("/leads", async (SaveLeadRequest body, CrmService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/crm/leads/{r.Lead.Id}", r)));
        g.MapPut("/leads/{id:guid}", async (Guid id, SaveLeadRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/leads/{id:guid}/move", async (Guid id, MoveLeadRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.MoveAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/leads/{id:guid}/assign", async (Guid id, AssignLeadRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.AssignAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/leads/{id:guid}/calls", async (Guid id, CallRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.CallAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/leads/{id:guid}/activities", async (Guid id, ActivityRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.ActivityAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/leads/{id:guid}/follow-up", async (Guid id, FollowUpRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.FollowUpAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/leads/{id:guid}/convert", async (Guid id, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.ConvertAsync(id, ct)).ToHttp(http));
        g.MapPost("/round-robin", async (RoundRobinRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.RoundRobinAsync(body, ct)).ToHttp(http, n => Results.Ok(new { assigned = n })));
        g.MapPost("/stages", async (SaveStageRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.SaveStageAsync(null, body, ct)).ToHttp(http));
        g.MapPut("/stages/{id:guid}", async (Guid id, SaveStageRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.SaveStageAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/sources", async (SaveSourceRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.SaveSourceAsync(null, body, ct)).ToHttp(http));
        g.MapPut("/sources/{id:guid}", async (Guid id, SaveSourceRequest body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.SaveSourceAsync(id, body, ct)).ToHttp(http));
        g.MapGet("/settings", async (CrmService svc, CancellationToken ct) => Results.Ok(await svc.SettingsAsync(ct)));
        g.MapPut("/settings", async (CrmSettingsDto body, CrmService svc, HttpContext http, CancellationToken ct) => (await svc.SaveSettingsAsync(body, ct)).ToHttp(http));
    }
}
