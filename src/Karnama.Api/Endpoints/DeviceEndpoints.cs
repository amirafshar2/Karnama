using System.Text;
using Karnama.Api.Infrastructure;
using Karnama.Application.Attendance;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>
/// N2: attendance devices. /iclock/* is the ZKTeco ADMS "push" protocol — anonymous by nature, so it only answers a
/// registered serial number calling from an allowed address, with a small body and its own rate limit (in production it
/// is served on a separate subdomain, see deploy/README). /api/v1/devices is the admin side.
/// </summary>
internal static class DeviceEndpoints
{
    public static void MapDevices(this IEndpointRouteBuilder app)
    {
        var adms = app.MapGroup("/iclock").WithTags("Devices").AllowAnonymous().RequireRateLimiting(RateLimits.Device).ExcludeFromDescription();
        foreach (var path in new[] { "/cdata", "/cdata.aspx" })
        {
            adms.MapGet(path, async (string? SN, DeviceService svc, HttpContext http, CancellationToken ct) =>
                Text(await svc.HandshakeAsync(SN, http.Connection.RemoteIpAddress, ct)));
            adms.MapPost(path, async (string? SN, string? table, long? Stamp, DeviceService svc, HttpContext http, CancellationToken ct) =>
            {
                var body = await ReadAsync(http, ct);
                return body is null ? Results.StatusCode(StatusCodes.Status413PayloadTooLarge) : Text(await svc.ReceiveAsync(SN, http.Connection.RemoteIpAddress, table, Stamp, body, ct));
            });
        }
        foreach (var path in new[] { "/getrequest", "/getrequest.aspx" })
            adms.MapGet(path, async (string? SN, DeviceService svc, HttpContext http, CancellationToken ct) => Text(await svc.PollAsync(SN, http.Connection.RemoteIpAddress, ct)));
        foreach (var path in new[] { "/devicecmd", "/devicecmd.aspx" })
            adms.MapPost(path, async (string? SN, DeviceService svc, HttpContext http, CancellationToken ct) =>
            {
                if (await ReadAsync(http, ct) is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                return Text(await svc.CommandResultAsync(SN, http.Connection.RemoteIpAddress, ct));
            });
        // Newer firmware registers first; we answer with the same option set as the handshake.
        adms.MapPost("/registry", async (string? SN, DeviceService svc, HttpContext http, CancellationToken ct) =>
        {
            if (await ReadAsync(http, ct) is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var r = await svc.HandshakeAsync(SN, http.Connection.RemoteIpAddress, ct);
            return r is null ? Text(null) : Text($"RegistryCode={SN}\n");
        });

        var g = app.MapGroup("/api/v1/devices").WithTags("Devices").RequireAuthorization().AddEndpointFilter<ValidationFilter>()
            .RequirePermission(Permissions.DevicesManage, Domain.Identity.PermissionScope.All);
        g.MapGet("/", async (DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.ListAsync(ct)).ToHttp(http));
        g.MapPost("/", async (SaveDeviceRequest body, DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(null, body, ct)).ToHttp(http));
        g.MapPut("/{id:guid}", async (Guid id, SaveDeviceRequest body, DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/sync-time", async (Guid id, DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.SyncTimeAsync(id, ct)).ToHttp(http));
        g.MapGet("/punches", async (Guid? device, DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.PunchesAsync(device, ct)).ToHttp(http));
        g.MapGet("/pins", async (DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.PinsAsync(ct)).ToHttp(http));
        g.MapPost("/pins", async (SavePinRequest body, DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.SavePinAsync(body, ct)).ToHttp(http));
        g.MapDelete("/pins/{id:guid}", async (Guid id, DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.DeletePinAsync(id, ct)).ToHttp(http));
        g.MapGet("/unknown-pins", async (DeviceService svc, HttpContext http, CancellationToken ct) => (await svc.UnknownPinsAsync(ct)).ToHttp(http));
        g.MapPost("/import/preview", async (IFormFile file, DeviceService svc, HttpContext http, CancellationToken ct) =>
            svc.Preview(await BytesAsync(file, ct), file.FileName).ToHttp(http))
            .DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxImport));
        g.MapPost("/{id:guid}/import", async (Guid id, IFormFile file, int pin, int date, int? time, int? status, bool header, DeviceService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ImportAsync(id, await BytesAsync(file, ct), file.FileName, new ImportMapping(pin, date, time, status, header), ct)).ToHttp(http))
            .DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxImport));
    }

    private const int MaxImport = 5 * 1024 * 1024;

    private static async Task<byte[]> BytesAsync(IFormFile f, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>The device protocol is plain text; an unknown device gets a bare 403 (no hint why).</summary>
    private static IResult Text(string? body) => body is null
        ? Results.Text("Forbidden", "text/plain", Encoding.UTF8, StatusCodes.Status403Forbidden)
        : Results.Text(body, "text/plain", Encoding.UTF8);

    /// <returns>The body, or null when it is larger than a device ever sends.</returns>
    private static async Task<string?> ReadAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength > DeviceService.MaxBodyBytes) return null;
        var buffer = new byte[DeviceService.MaxBodyBytes + 1];
        var total = 0;
        int n;
        while ((n = await http.Request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)) > 0)
        {
            total += n;
            if (total > DeviceService.MaxBodyBytes) return null;
        }
        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}
