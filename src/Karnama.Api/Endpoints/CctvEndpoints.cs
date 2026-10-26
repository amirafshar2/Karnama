using Karnama.Api.Infrastructure;
using Karnama.Application.Attendance;
using Karnama.Application.Cctv;
using Karnama.Infrastructure.Cctv;
using Microsoft.Extensions.Options;

namespace Karnama.Api.Endpoints;

/// <summary>N11: office cameras. Permissions are checked in <see cref="CctvService"/>; the MediaMTX hook only answers the box's tunnel address.</summary>
internal static class CctvEndpoints
{
    public static void MapCctv(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/cctv").WithTags("CCTV").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        g.MapGet("/", async (CctvService svc, HttpContext http, CancellationToken ct) => (await svc.ListAsync(ct)).ToHttp(http));
        g.MapPost("/cameras", async (SaveCameraRequest body, CctvService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(null, body, ct)).ToHttp(http));
        g.MapPut("/cameras/{id:guid}", async (Guid id, SaveCameraRequest body, CctvService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(id, body, ct)).ToHttp(http));
        g.MapDelete("/cameras/{id:guid}", async (Guid id, CctvService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        g.MapPost("/resync", async (CctvService svc, HttpContext http, CancellationToken ct) => (await svc.ResyncAsync(ct)).ToHttp(http));
        g.MapGet("/cameras/{id:guid}/snapshot", async (Guid id, CctvService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.SnapshotAsync(id, ct);
            if (r.IsFailure) return r.ToHttp(http);
            http.Response.Headers.CacheControl = "private, max-age=5";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(r.Value!, "image/jpeg");
        });
        g.MapPost("/cameras/{id:guid}/live", async (Guid id, bool? high, CctvService svc, HttpContext http, CancellationToken ct) => (await svc.StartLiveAsync(id, high == true, ct)).ToHttp(http));
        g.MapPost("/cameras/{id:guid}/live/stop", async (Guid id, LiveStopRequest body, CctvService svc, HttpContext http, CancellationToken ct) => (await svc.StopLiveAsync(id, body, ct)).ToHttp(http));

        // MediaMTX "authHTTPAddress": called by the box for every read. Only the box (through the tunnel) may ask.
        app.MapPost("/api/v1/cctv/media-auth", (MediaAuthRequest body, CctvService svc, IOptions<CctvOptions> o, HttpContext http) =>
        {
            var hooks = o.Value.HookAddresses;
            if (hooks.Count == 0 || !AttendanceSettingsStore.InNetworks(http.Connection.RemoteIpAddress, hooks)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return svc.AuthorizeMedia(body) ? Results.Ok() : Results.StatusCode(StatusCodes.Status401Unauthorized);
        })
        // Every viewer's check arrives from the one box address: a shared rate limit would let anyone lock all viewers
        // out by spamming the public WHEP URL. The tunnel-address gate above is the protection here.
        .AllowAnonymous().DisableRateLimiting().ExcludeFromDescription();
    }
}
