using Karnama.Api.Infrastructure;
using Karnama.Application.Chat;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N6: in-app chat. Access rules (who may talk to whom, membership) are enforced in <see cref="ChatService"/>.</summary>
internal static class ChatEndpoints
{
    public sealed record DirectRequest(Guid UserId);
    public sealed record ReadRequest(long UpTo);
    public sealed record MuteRequest(bool Muted);

    public static void MapChat(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/chat").WithTags("Chat").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        g.MapGet("/", async (ChatService svc, HttpContext http, CancellationToken ct) => (await svc.ListAsync(ct)).ToHttp(http));
        g.MapGet("/unread", async (ChatService svc, CancellationToken ct) => Results.Ok(new { count = await svc.UnreadTotalAsync(ct) }));
        g.MapGet("/contacts", async (ChatService svc, HttpContext http, CancellationToken ct) => (await svc.ContactsAsync(ct)).ToHttp(http));
        g.MapGet("/search", async (string q, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.SearchAsync(q, ct)).ToHttp(http));
        g.MapPost("/direct", async (DirectRequest body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.DirectAsync(body.UserId, ct)).ToHttp(http));
        g.MapPost("/groups", async (GroupRequest body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.CreateGroupAsync(body, ct)).ToHttp(http));
        g.MapPut("/groups/{id:guid}", async (Guid id, GroupRequest body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateGroupAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/orders/{orderId:guid}", async (Guid orderId, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.ForOrderAsync(orderId, ct)).ToHttp(http));
        g.MapGet("/{id:guid}/messages", async (Guid id, long? after, long? before, int? limit, ChatService svc, HttpContext http, CancellationToken ct) =>
            (await svc.MessagesAsync(id, after, before, limit ?? 40, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/messages", async (Guid id, SendTextRequest body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.SendTextAsync(id, body, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/files", async (Guid id, IFormFile file, bool? voice, int? duration, string? caption, Guid? clientKey, ChatService svc, HttpContext http, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return (await svc.UploadAsync(id, s, file.Length, file.FileName, voice == true, duration, caption, clientKey ?? Guid.Empty, ct)).ToHttp(http);
        }).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(ChatService.MaxFileBytes + 64 * 1024));
        g.MapPost("/{id:guid}/read", async (Guid id, ReadRequest body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.ReadAsync(id, body.UpTo, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/mute", async (Guid id, MuteRequest body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.MuteAsync(id, body.Muted, ct)).ToHttp(http));
        g.MapPut("/messages/{messageId:long}", async (long messageId, EditMessageRequest body, ChatService svc, HttpContext http, CancellationToken ct) =>
            (await svc.EditAsync(messageId, body, ct)).ToHttp(http));
        g.MapDelete("/messages/{messageId:long}", async (long messageId, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(messageId, ct)).ToHttp(http));
        g.MapGet("/files/{messageId:long}", async (long messageId, ChatService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.OpenFileAsync(messageId, ct);
            if (r.IsFailure) return r.ToHttp(http);
            var (m, content) = r.Value;
            http.Response.Headers.CacheControl = "private, max-age=86400";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            // Images and voice play inline; anything else downloads (never rendered by the browser).
            var inline = m.Kind is Karnama.Domain.Chat.MessageKind.Image or Karnama.Domain.Chat.MessageKind.Voice;
            return Results.Stream(content, m.ContentType ?? "application/octet-stream", inline ? null : m.FileName, enableRangeProcessing: true);
        });

        var settings = app.MapGroup("/api/v1/settings/chat").WithTags("Settings").AddEndpointFilter<ValidationFilter>();
        settings.MapGet("/", async (ChatService svc, CancellationToken ct) => Results.Ok(await svc.SettingsAsync(ct))).RequirePermission(Permissions.SettingsManage, PermissionScope.All);
        settings.MapPut("/", async (ChatSettingsDto body, ChatService svc, HttpContext http, CancellationToken ct) => (await svc.SaveSettingsAsync(body, ct)).ToHttp(http))
            .RequirePermission(Permissions.SettingsManage, PermissionScope.All);
    }
}
