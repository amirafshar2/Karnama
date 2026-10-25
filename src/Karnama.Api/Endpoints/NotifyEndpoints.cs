using System.Text.Json;
using Karnama.Api.Infrastructure;
using Karnama.Application.Notify;
using Karnama.Application.Publishing;
using Karnama.Domain.Identity;
using Karnama.Domain.Notify;
using Karnama.Infrastructure.Notify;
using Microsoft.Extensions.Options;

namespace Karnama.Api.Endpoints;

/// <summary>M6: the bell (inbox + prefs + bot linking), bot webhooks, the publishing calendar and client messages.</summary>
internal static class NotifyEndpoints
{
    public sealed record ReadRequest(Guid? Id);

    public static void MapNotify(this IEndpointRouteBuilder app)
    {
        var inbox = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        inbox.MapGet("/", async (InboxService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        inbox.MapPost("/read", async (ReadRequest body, InboxService svc, CancellationToken ct) => { await svc.ReadAsync(body.Id, ct); return Results.NoContent(); });
        inbox.MapGet("/prefs", async (InboxService svc, CancellationToken ct) => Results.Ok(await svc.PrefsAsync(ct)));
        inbox.MapPut("/prefs", async (UpdatePrefsRequest body, InboxService svc, CancellationToken ct) => Results.Ok(await svc.UpdatePrefsAsync(body, ct)));
        inbox.MapPost("/links/{messenger}", async (string messenger, InboxService svc, HttpContext http, CancellationToken ct) =>
            CatalogEndpoints.E<Messenger>(messenger) is { } m ? (await svc.LinkAsync(m, ct)).ToHttp(http) : Results.NotFound());
        inbox.MapDelete("/links/{messenger}", async (string messenger, InboxService svc, CancellationToken ct) =>
        {
            if (CatalogEndpoints.E<Messenger>(messenger) is not { } m) return Results.NotFound();
            await svc.UnlinkAsync(m, ct);
            return Results.NoContent();
        });

        // ---- Bots. Anonymous by nature, so each is authenticated by its own secret; anything malformed gets a quiet 200/401. ----
        var bots = app.MapGroup("/api/v1/bots").WithTags("Bots").AllowAnonymous().DisableRateLimiting();
        bots.MapPost("/bale/{secret}", async (string secret, HttpContext http, IOptions<MessengerOptions> o, BotUpdateService svc, IMessengerSender send,
            ILoggerFactory logs, CancellationToken ct) =>
        {
            var expected = o.Value.BaleWebhookSecret;
            if (string.IsNullOrEmpty(expected) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(expected))) return Results.NotFound();
            if (await ReadBodyAsync(http, ct) is not { } body) return Results.Ok();
            var u = ParseBale(body);
            if (u is null) return Results.Ok();
            await ReplyAsync(Messenger.Bale, u, svc, send, logs, ct);
            return Results.Ok();
        });
        bots.MapPost("/telegram", async (HttpContext http, IOptions<MessengerOptions> o, BotUpdateService svc, IMessengerSender send, TimeProvider clock,
            ILoggerFactory logs, CancellationToken ct) =>
        {
            if (await ReadBodyAsync(http, ct) is not { } body) return Results.Ok();
            if (!GatewaySignature.Verify(o.Value.TelegramGatewaySecret, http.Request.Headers[GatewaySignature.TimestampHeader], http.Request.Headers[GatewaySignature.SignatureHeader], body, clock.GetUtcNow()))
                return Results.Unauthorized();
            BotUpdateService.Incoming? u;
            try { u = JsonSerializer.Deserialize<BotUpdateService.Incoming>(body, JsonSerializerOptions.Web); } catch (JsonException) { return Results.Ok(); }
            if (u is null || u.ChatId == 0) return Results.Ok();
            await ReplyAsync(Messenger.Telegram, u, svc, send, logs, ct);
            return Results.Ok();
        });

        // ---- Publishing calendar (staff) ----
        var pub = app.MapGroup("/api/v1/publishing").WithTags("Publishing").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        pub.MapGet("/", async (DateTimeOffset from, DateTimeOffset to, Guid? clientId, PublishService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ListAsync(from, to, clientId, ct)).ToHttp(http));
        pub.MapPost("/", async (UpsertPublishRequest body, PublishService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/publishing/{r.Id}", r)));
        pub.MapPut("/{id:guid}", async (Guid id, UpsertPublishRequest body, PublishService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        pub.MapPost("/{id:guid}/cancel", async (Guid id, PublishService svc, HttpContext http, CancellationToken ct) => (await svc.CancelAsync(id, ct)).ToHttp(http));
        pub.MapPost("/{id:guid}/published", async (Guid id, MarkPublishedRequest body, PublishService svc, HttpContext http, CancellationToken ct) =>
            (await svc.MarkPublishedAsync(id, body, ct)).ToHttp(http));

        // ---- Portal: reminders for pages the client runs itself, and messages to the agency ----
        var portal = app.MapGroup("/api/v1/portal").WithTags("Portal").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        portal.MapGet("/publishing", async (PublishService svc, HttpContext http, CancellationToken ct) => (await svc.PortalAsync(ct)).ToHttp(http));
        portal.MapPost("/publishing/{id:guid}/published", async (Guid id, MarkPublishedRequest body, PublishService svc, HttpContext http, CancellationToken ct) =>
            (await svc.MarkPublishedAsync(id, body, ct)).ToHttp(http));
        portal.MapPost("/orders/{id:guid}/messages", async (Guid id, Karnama.Application.Portal.PortalMessageRequest body, Karnama.Application.Portal.PortalService svc, HttpContext http, CancellationToken ct) =>
            (await svc.SendMessageAsync(id, body, ct)).ToHttp(http));
    }

    private static async Task<string?> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength is > 64 * 1024) return null;
        using var reader = new StreamReader(http.Request.Body);
        var buffer = new char[64 * 1024 + 1];
        var n = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
        return n > 64 * 1024 ? null : new string(buffer, 0, n);
    }

    /// <summary>Bale uses the Telegram Bot API shape: {update_id, message:{chat:{id}, text}}.</summary>
    internal static BotUpdateService.Incoming? ParseBale(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("update_id", out var id) || !root.TryGetProperty("message", out var msg)) return null;
            if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatId)) return null;
            var text = msg.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            return new BotUpdateService.Incoming(id.GetInt64(), chatId.GetInt64(), text);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static async Task ReplyAsync(Messenger m, BotUpdateService.Incoming u, BotUpdateService svc, IMessengerSender send, ILoggerFactory logs, CancellationToken ct)
    {
        var reply = await svc.HandleAsync(m, u, ct);
        if (reply is null) return;
        try { await send.SendAsync(m, u.ChatId, reply, ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logs.CreateLogger("Bots").LogWarning("Bot reply via {Messenger} failed: {Error}", m, e.GetType().Name);
        }
    }
}
