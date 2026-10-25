using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Karnama.TelegramGateway;

namespace Karnama.TelegramGateway;

// Karnama Telegram gateway (PROMPT2 §8): runs on a small server OUTSIDE Iran. Telegram's webhook arrives here, is queued on
// disk and forwarded (signed) to the Iran API; the Iran API asks this service (signed) to call the Bot API. It stores no
// personal data: only the update queue. File bytes never pass through here except tiny previews (≤ 1 MB).
public static class GatewayProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = GatewayOptions.From(builder.Configuration);
        var problems = options.Problems().ToList();
        if (problems.Count > 0 && !builder.Environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Gateway settings missing or weak: " + string.Join(", ", problems));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<UpdateQueue>();
        builder.Services.AddSingleton<ReplayGuard>();
        builder.Services.AddSingleton<TelegramApi>();
        builder.Services.AddHttpClient("telegram", c => { c.BaseAddress = new Uri(options.TelegramApi); c.Timeout = TimeSpan.FromSeconds(30); });
        builder.Services.AddHttpClient("iran", c =>
        {
            if (Uri.TryCreate(options.IranApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var u)) c.BaseAddress = u;
            c.Timeout = TimeSpan.FromSeconds(20);
        });
        builder.Services.AddHostedService<Forwarder>();
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1024 * 1024);

        var app = builder.Build();

        app.MapGet("/health", (UpdateQueue q) => Results.Ok(new { status = "ok", queued = q.Count }));

        // Telegram → here. Secret path + Telegram's secret_token header, both compared in constant time.
        app.MapPost("/tg/{path}", async (string path, HttpContext http, UpdateQueue queue) =>
        {
            if (!Same(path, options.WebhookPath) || !Same(http.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString(), options.WebhookSecret))
                return Results.NotFound();
            var body = await ReadAsync(http, 1024 * 1024);
            if (body is null) return Results.Ok();
            long updateId;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("update_id", out var id) || !id.TryGetInt64(out updateId)) return Results.Ok();
            }
            catch (JsonException) { return Results.Ok(); }
            // Full queue = ask Telegram to retry later rather than losing the update.
            return queue.Enqueue(updateId, body) ? Results.Ok() : Results.StatusCode(503);
        });

        // Iran → here. Signed, fresh (5 minutes) and never replayed.
        var v1 = app.MapGroup("/v1").AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var body = await ReadAsync(http, 64 * 1024);
            var signature = http.Request.Headers[Signature.SignatureHeader].ToString();
            var clock = http.RequestServices.GetRequiredService<TimeProvider>();
            if (body is null || !Signature.Verify(options.Secret, http.Request.Headers[Signature.TimestampHeader], signature, body, clock.GetUtcNow())
                || !http.RequestServices.GetRequiredService<ReplayGuard>().FirstTime(signature))
                return Results.Unauthorized();
            http.Items["body"] = body;
            return await next(ctx);
        });

        v1.MapPost("/call", async (HttpContext http, TelegramApi tg, CancellationToken ct) =>
        {
            JsonNode? req;
            try { req = JsonNode.Parse((string)http.Items["body"]!); } catch (JsonException) { return Results.BadRequest(); }
            var method = req?["method"]?.GetValue<string>();
            if (method is null || !TelegramApi.Allowed.Contains(method)) return Results.BadRequest(new { ok = false, error_code = 400, description = "method not allowed" });
            try
            {
                var (status, answer) = await tg.CallAsync(method, req!["args"], ct);
                // Telegram's own answer goes back as is (the Iran side decides retry vs final from error_code).
                return Results.Content(answer, "application/json", Encoding.UTF8, status is >= 200 and < 500 ? 200 : 502);
            }
            catch (HttpRequestException) { return Results.StatusCode(502); }
        });

        // Old short form used by notifications: {chatId, text}.
        v1.MapPost("/send", async (HttpContext http, TelegramApi tg, CancellationToken ct) =>
        {
            JsonNode? req;
            try { req = JsonNode.Parse((string)http.Items["body"]!); } catch (JsonException) { return Results.BadRequest(); }
            if (req?["chatId"] is null || req["text"] is null) return Results.BadRequest();
            var (status, answer) = await tg.CallAsync("sendMessage", new JsonObject { ["chat_id"] = req["chatId"]!.DeepClone(), ["text"] = req["text"]!.DeepClone() }, ct);
            return status == 200 ? Results.Ok() : Results.Content(answer, "application/json", Encoding.UTF8, status >= 500 ? 502 : 422);
        });

        v1.MapPost("/thumb", async (HttpContext http, TelegramApi tg, CancellationToken ct) =>
        {
            JsonNode? req;
            try { req = JsonNode.Parse((string)http.Items["body"]!); } catch (JsonException) { return Results.BadRequest(); }
            var fileId = req?["fileId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(fileId) || fileId.Length > 200) return Results.BadRequest();
            var bytes = await tg.ThumbAsync(fileId, ct);
            return bytes is null ? Results.NotFound() : Results.Bytes(bytes, "application/octet-stream");
        });

        if (!app.Environment.IsEnvironment("Testing"))
            _ = Task.Run(async () =>
            {
                try { await app.Services.GetRequiredService<TelegramApi>().RegisterWebhookAsync(app.Logger, CancellationToken.None); }
                catch (Exception ex) { app.Logger.LogWarning("Webhook registration failed: {Error}", ex.GetType().Name); }
            });

        await app.RunAsync();
    }

    private static bool Same(string a, string b) =>
        !string.IsNullOrEmpty(b) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static async Task<string?> ReadAsync(HttpContext http, int max)
    {
        http.Request.EnableBuffering();
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
        var buffer = new char[max + 1];
        var n = await reader.ReadBlockAsync(buffer.AsMemory());
        http.Request.Body.Position = 0;
        return n > max ? null : new string(buffer, 0, n);
    }
}

/// <summary>Marker for WebApplicationFactory in tests.</summary>
public sealed class GatewayEntry;
