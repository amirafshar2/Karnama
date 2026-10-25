using Karnama.Api.Infrastructure;
using Karnama.Application.Common;
using Karnama.Application.Media;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N5: the files inbox (files people sent to the bot) and assigning them inside the messenger.</summary>
internal static class MediaEndpoints
{
    public static void MapMedia(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/media").WithTags("Media").AddEndpointFilter<ValidationFilter>();
        g.MapGet("/", async (string? cursor, int? limit, string? status, bool? open, MediaService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ListAsync(new KeysetQuery(cursor, limit ?? 30), CatalogEndpoints.E<Karnama.Domain.Media.MediaStatus>(status), open == true, ct)).ToHttp(http))
            .RequirePermission(Permissions.MediaInbox, PermissionScope.All);
        g.MapGet("/targets", async (MediaService svc, HttpContext http, CancellationToken ct) => (await svc.TargetsAsync(ct)).ToHttp(http))
            .RequirePermission(Permissions.MediaAssign, PermissionScope.All);
        g.MapGet("/{id:guid}", async (Guid id, MediaService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http))
            .RequirePermission(Permissions.MediaInbox, PermissionScope.All);
        g.MapGet("/{id:guid}/thumb", async (Guid id, MediaService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.ThumbnailAsync(id, ct);
            if (r.IsFailure) return r.ToHttp(http);
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Stream(r.Value.Stream, r.Value.ContentType);
        }).RequirePermission(Permissions.MediaInbox, PermissionScope.All);
        g.MapPost("/{id:guid}/assign", async (Guid id, AssignMediaRequest body, MediaService svc, HttpContext http, CancellationToken ct) => (await svc.AssignAsync(id, body, ct)).ToHttp(http))
            .RequirePermission(Permissions.MediaAssign, PermissionScope.All);
    }
}
