using Karnama.Api.Infrastructure;
using Karnama.Application.Help;

namespace Karnama.Api.Endpoints;

/// <summary>N12: help center. Readable by every signed-in user (filtered by audience); editable with content.manage.</summary>
internal static class HelpEndpoints
{
    public static void MapHelp(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/help").WithTags("Help").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        g.MapGet("/", async (string? q, string? category, HelpService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(q, category, ct)));
        g.MapGet("/page", async (string key, HelpService svc, CancellationToken ct) => await svc.ForPageAsync(key, ct) is { } a ? Results.Ok(a) : Results.NoContent());
        g.MapGet("/{slug}", async (string slug, HelpService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(slug, ct)).ToHttp(http));
        g.MapPost("/", async (SaveHelpRequest body, HelpService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(null, body, ct)).ToHttp(http));
        g.MapPut("/{id:guid}", async (Guid id, SaveHelpRequest body, HelpService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(id, body, ct)).ToHttp(http));
        g.MapDelete("/{id:guid}", async (Guid id, HelpService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
    }
}
