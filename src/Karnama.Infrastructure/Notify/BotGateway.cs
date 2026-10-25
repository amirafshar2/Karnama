using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Karnama.Application.Abstractions;
using Karnama.Domain.Notify;
using Microsoft.Extensions.Options;

namespace Karnama.Infrastructure.Notify;

/// <summary>
/// Bot API calls. Telegram: the signed gateway outside Iran (<c>v1/call</c>, <c>v1/thumb</c>); Bale: its Bot API directly.
/// Only a fixed list of methods can be called. Bot tokens and the gateway key come from the environment only.
/// </summary>
internal sealed class BotGateway(IHttpClientFactory http, IOptions<MessengerOptions> options, TimeProvider clock) : IBotGateway
{
    public static readonly HashSet<string> Allowed = ["sendMessage", "copyMessage", "answerCallbackQuery", "editMessageReplyMarkup"];
    private const int MaxThumbBytes = 1024 * 1024;

    public bool IsConfigured(Messenger m) => m == Messenger.Telegram
        ? !string.IsNullOrEmpty(options.Value.TelegramGatewayUrl) && !string.IsNullOrEmpty(options.Value.TelegramGatewaySecret)
        : !string.IsNullOrEmpty(options.Value.BaleToken);

    public async Task<BotCallResult> CallAsync(Messenger m, string method, JsonObject args, CancellationToken ct)
    {
        if (!Allowed.Contains(method)) return new BotCallResult(false, true, null, "method not allowed");
        HttpResponseMessage resp;
        if (m == Messenger.Bale)
        {
            using var content = new StringContent(args.ToJsonString(), Encoding.UTF8, "application/json");
            resp = await http.CreateClient("bale").PostAsync($"bot{options.Value.BaleToken}/{method}", content, ct);
        }
        else
        {
            var body = new JsonObject { ["method"] = method, ["args"] = args.DeepClone() }.ToJsonString();
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/call") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            GatewaySignature.Sign(req, body, options.Value.TelegramGatewaySecret, clock.GetUtcNow());
            resp = await http.CreateClient("telegram-gateway").SendAsync(req, ct);
        }
        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            return Interpret(resp.StatusCode, text);
        }
    }

    /// <summary>Bot API answers {ok, result} or {ok:false, error_code, description}. 400/403 are final; 429/5xx are retried.</summary>
    internal static BotCallResult Interpret(HttpStatusCode status, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return new BotCallResult(true, false, root.TryGetProperty("result", out var r) ? r.GetRawText() : null, null);
            var code = root.TryGetProperty("error_code", out var c) && c.TryGetInt32(out var cv) ? cv : (int)status;
            var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new BotCallResult(false, code is 400 or 403 or 404, null, $"{code}: {description}");
        }
        catch (JsonException)
        {
            return new BotCallResult(false, status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, null, $"{(int)status}");
        }
    }

    public async Task<byte[]?> ThumbnailAsync(Messenger m, string fileId, CancellationToken ct)
    {
        if (m == Messenger.Bale)
        {
            var client = http.CreateClient("bale");
            var token = options.Value.BaleToken;
            using var info = await client.PostAsync($"bot{token}/getFile", new StringContent(new JsonObject { ["file_id"] = fileId }.ToJsonString(), Encoding.UTF8, "application/json"), ct);
            var r = Interpret(info.StatusCode, await info.Content.ReadAsStringAsync(ct));
            if (!r.Ok || r.ResultJson is null) return null;
            using var doc = JsonDocument.Parse(r.ResultJson);
            if (!doc.RootElement.TryGetProperty("file_path", out var path) || path.GetString() is not { } p) return null;
            if (doc.RootElement.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var sv) && sv > MaxThumbBytes) return null;
            return await ReadCappedAsync(await client.GetAsync($"file/bot{token}/{p}", HttpCompletionOption.ResponseHeadersRead, ct), ct);
        }
        var body = new JsonObject { ["fileId"] = fileId }.ToJsonString();
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/thumb") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        GatewaySignature.Sign(req, body, options.Value.TelegramGatewaySecret, clock.GetUtcNow());
        return await ReadCappedAsync(await http.CreateClient("telegram-gateway").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct), ct);
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        using (resp)
        {
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxThumbBytes) return null;
            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int n;
            while ((n = await s.ReadAsync(buffer, ct)) > 0)
            {
                if (ms.Length + n > MaxThumbBytes) return null;
                ms.Write(buffer, 0, n);
            }
            return ms.ToArray();
        }
    }
}
