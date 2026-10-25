using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Karnama.TelegramGateway;

/// <summary>
/// Settings (environment only, never in the repo):
/// Telegram__BotToken, Telegram__WebhookSecret (Telegram's secret_token header), Telegram__WebhookPath (secret URL segment),
/// Gateway__IranApiUrl (https://api.example.ir), Gateway__Secret (HMAC key shared with the Iran API),
/// Gateway__PublicUrl (https://this-gateway, to register the webhook at startup), Gateway__QueuePath.
/// </summary>
public sealed class GatewayOptions
{
    public string BotToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string WebhookPath { get; set; } = "";
    public string IranApiUrl { get; set; } = "";
    public string Secret { get; set; } = "";
    public string? PublicUrl { get; set; }
    public string QueuePath { get; set; } = "queue";
    public string TelegramApi { get; set; } = "https://api.telegram.org/";

    public static GatewayOptions From(IConfiguration c) => new()
    {
        BotToken = c["Telegram:BotToken"] ?? "",
        WebhookSecret = c["Telegram:WebhookSecret"] ?? "",
        WebhookPath = c["Telegram:WebhookPath"] ?? "",
        IranApiUrl = c["Gateway:IranApiUrl"] ?? "",
        Secret = c["Gateway:Secret"] ?? "",
        PublicUrl = c["Gateway:PublicUrl"],
        QueuePath = c["Gateway:QueuePath"] ?? "queue",
        TelegramApi = c["Telegram:ApiBase"] ?? "https://api.telegram.org/",
    };

    public IEnumerable<string> Problems()
    {
        if (BotToken.Length < 20) yield return "Telegram__BotToken";
        if (WebhookSecret.Length < 16) yield return "Telegram__WebhookSecret (16+ chars, A-Z a-z 0-9 _ -)";
        if (WebhookPath.Length < 16) yield return "Telegram__WebhookPath (16+ random chars)";
        if (Secret.Length < 32) yield return "Gateway__Secret (32+ random chars, same as Messengers__TelegramGatewaySecret in Iran)";
        if (!Uri.TryCreate(IranApiUrl, UriKind.Absolute, out var u) || (u.Scheme != "https" && !u.IsLoopback)) yield return "Gateway__IranApiUrl (https)";
    }
}

/// <summary>Same scheme as Karnama.Infrastructure.Notify.GatewaySignature (a unit test keeps them identical).</summary>
public static class Signature
{
    public const string TimestampHeader = "X-Karnama-Timestamp";
    public const string SignatureHeader = "X-Karnama-Signature";

    public static string Compute(string secret, long timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

    public static void Sign(HttpRequestMessage req, string body, string secret, DateTimeOffset now)
    {
        var ts = now.ToUnixTimeSeconds();
        req.Headers.Add(TimestampHeader, ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        req.Headers.Add(SignatureHeader, Compute(secret, ts, body));
    }

    public static bool Verify(string secret, string? timestamp, string? signature, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || !long.TryParse(timestamp, out var ts) || string.IsNullOrEmpty(signature)) return false;
        if (Math.Abs(now.ToUnixTimeSeconds() - ts) > 300) return false;
        var expected = Encoding.ASCII.GetBytes(Compute(secret, ts, body));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature));
    }
}

/// <summary>Remembers signatures already used (10 minutes, longer than the 5-minute window): a captured request can't be replayed.</summary>
public sealed class ReplayGuard(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

    public bool FirstTime(string signature)
    {
        var now = clock.GetUtcNow();
        if (_seen.Count > 50_000)
            foreach (var kv in _seen.Where(kv => kv.Value < now.AddMinutes(-10)).ToList()) _seen.TryRemove(kv.Key, out _);
        return _seen.TryAdd(signature, now);
    }
}

/// <summary>
/// Updates waiting to reach Iran: one file per update on disk, so an international outage or a restart loses nothing.
/// The file name is the update id (Telegram re-sending the same update just overwrites it).
/// </summary>
public sealed class UpdateQueue
{
    public const int MaxFiles = 20_000;
    private readonly string _dir;

    public UpdateQueue(GatewayOptions o)
    {
        _dir = Path.GetFullPath(o.QueuePath);
        Directory.CreateDirectory(_dir);
    }

    public int Count => Directory.EnumerateFiles(_dir, "*.json").Count();

    public bool Enqueue(long updateId, string json)
    {
        if (Count >= MaxFiles) return false;
        var final = Path.Combine(_dir, $"{updateId:D20}.json");
        var temp = final + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, final, overwrite: true);
        return true;
    }

    public IReadOnlyList<string> Next(int count) => Directory.EnumerateFiles(_dir, "*.json").Order(StringComparer.Ordinal).Take(count).ToList();
}

/// <summary>Sends queued updates to the Iran API (signed), oldest first; keeps them until the API says OK.</summary>
public sealed class Forwarder(UpdateQueue queue, GatewayOptions o, IHttpClientFactory http, TimeProvider clock, ILogger<Forwarder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            int sent;
            try { sent = await ForwardOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Forwarding paused: {Error}", ex.GetType().Name);
                sent = -1;
            }
            delay = sent > 0 ? TimeSpan.FromMilliseconds(200) : sent == 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { }
        }
    }

    /// <returns>How many were delivered; -1 when Iran is unreachable (back off).</returns>
    public async Task<int> ForwardOnceAsync(CancellationToken ct)
    {
        var sent = 0;
        foreach (var file in queue.Next(20))
        {
            if (File.GetLastWriteTimeUtc(file) < clock.GetUtcNow().UtcDateTime.AddDays(-7))
            {
                logger.LogWarning("Dropping an update older than 7 days");
                File.Delete(file);
                continue;
            }
            string body;
            try { body = await File.ReadAllTextAsync(file, ct); }
            catch (FileNotFoundException) { continue; }   // another pass already delivered it
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/bots/telegram") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            Signature.Sign(req, body, o.Secret, clock.GetUtcNow());
            using var resp = await http.CreateClient("iran").SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Iran API answered {Status}", (int)resp.StatusCode);
                return -1;
            }
            File.Delete(file);
            sent++;
        }
        return sent;
    }
}

/// <summary>Bot API calls on behalf of the Iran API. Only these methods; anything else is refused.</summary>
public sealed class TelegramApi(IHttpClientFactory http, GatewayOptions o)
{
    public static readonly HashSet<string> Allowed = ["sendMessage", "copyMessage", "answerCallbackQuery", "editMessageReplyMarkup"];
    public const int MaxThumbBytes = 1024 * 1024;

    public async Task<(int Status, string Body)> CallAsync(string method, JsonNode? args, CancellationToken ct)
    {
        using var content = new StringContent(args?.ToJsonString() ?? "{}", Encoding.UTF8, "application/json");
        using var resp = await http.CreateClient("telegram").PostAsync($"bot{o.BotToken}/{method}", content, ct);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }

    public async Task<byte[]?> ThumbAsync(string fileId, CancellationToken ct)
    {
        var (_, body) = await CallAsync("getFile", new JsonObject { ["file_id"] = fileId }, ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
        var file = root.GetProperty("result");
        if (file.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var s) && s > MaxThumbBytes) return null;
        if (!file.TryGetProperty("file_path", out var p) || p.GetString() is not { } path) return null;
        using var resp = await http.CreateClient("telegram").GetAsync($"file/bot{o.BotToken}/{path}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxThumbBytes) return null;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length > MaxThumbBytes ? null : bytes;
    }

    public async Task RegisterWebhookAsync(ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(o.PublicUrl)) return;
        var (status, body) = await CallAsync("setWebhook", new JsonObject
        {
            ["url"] = $"{o.PublicUrl.TrimEnd('/')}/tg/{o.WebhookPath}",
            ["secret_token"] = o.WebhookSecret,
            ["allowed_updates"] = new JsonArray("message", "callback_query"),
            ["max_connections"] = 10,
        }, ct);
        logger.LogInformation("setWebhook answered {Status}", status);
        if (status != 200) logger.LogWarning("Webhook registration failed: {Body}", body.Length > 200 ? body[..200] : body);
    }
}
