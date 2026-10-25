using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Notify;
using Karnama.Domain.Notify;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karnama.Infrastructure.Notify;

public sealed class MessengerOptions
{
    public const string Section = "Messengers";
    /// <summary>Bale bot token (env only). Empty = Bale off.</summary>
    public string BaleToken { get; set; } = string.Empty;
    /// <summary>Secret path segment of the Bale webhook.</summary>
    public string BaleWebhookSecret { get; set; } = string.Empty;
    /// <summary>Telegram goes through Karnama.TelegramGateway outside Iran (PROMPT2 §8). Empty = Telegram off.</summary>
    public string TelegramGatewayUrl { get; set; } = string.Empty;
    /// <summary>Shared HMAC key between this API and the gateway (env only).</summary>
    public string TelegramGatewaySecret { get; set; } = string.Empty;
}

/// <summary>Sends a text to one linked chat. Bale talks to its Bot API directly; Telegram goes through the signed gateway.</summary>
public interface IMessengerSender
{
    Task SendAsync(Messenger messenger, long chatId, string text, CancellationToken ct);
}

internal sealed class MessengerSender(IHttpClientFactory http, IOptions<MessengerOptions> options) : IMessengerSender
{
    public async Task SendAsync(Messenger messenger, long chatId, string text, CancellationToken ct)
    {
        var o = options.Value;
        if (messenger == Messenger.Bale)
        {
            if (string.IsNullOrEmpty(o.BaleToken)) throw new InvalidOperationException("Bale is not configured");
            using var res = await http.CreateClient("bale").PostAsJsonAsync($"bot{o.BaleToken}/sendMessage", new { chat_id = chatId, text }, ct);
            res.EnsureSuccessStatusCode();
            return;
        }
        if (string.IsNullOrEmpty(o.TelegramGatewayUrl)) throw new InvalidOperationException("Telegram gateway is not configured");
        var body = JsonSerializer.Serialize(new { chatId, text });
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/send") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        GatewaySignature.Sign(req, body, o.TelegramGatewaySecret, DateTimeOffset.UtcNow);
        using var resp = await http.CreateClient("telegram-gateway").SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>HMAC-SHA256 over "timestamp.body", sent in headers; verified both ways with a 5-minute window.</summary>
public static class GatewaySignature
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
        if (Math.Abs(now.ToUnixTimeSeconds() - ts) > 300) return false;   // replay window
        var expected = Encoding.ASCII.GetBytes(Compute(secret, ts, body));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature));
    }
}

/// <summary>
/// Sends the outbox: every few seconds takes due messages (FOR UPDATE SKIP LOCKED, so several workers never send the
/// same row), sends them, retries with backoff. Runs in Karnama.Worker.
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DispatchOnceAsync(stoppingToken) == 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch failed");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();
        var messengers = scope.ServiceProvider.GetRequiredService<IMessengerSender>();
        var baseUrl = scope.ServiceProvider.GetRequiredService<IOptions<NotifyOptions>>().Value.PublicBaseUrl.TrimEnd('/');
        var now = clock.GetUtcNow();

        // Claim a batch in one statement: pushing DueAt forward is a 5-minute lease, so a second worker skips these rows and a
        // crashed worker's rows come back by themselves. No transaction is held open while we talk to SMS/Bale/Telegram.
        var lease = now.AddMinutes(5);
        var batch = await db.Outbox.FromSqlInterpolated($"""
            UPDATE outbox SET "DueAt" = {lease}
            WHERE "Id" IN (SELECT "Id" FROM outbox WHERE "Status" = 0 AND "DueAt" <= {now} ORDER BY "Id" LIMIT 50 FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).ToListAsync(ct);
        foreach (var m in batch)
        {
            var text = m.Link is null ? m.Text : $"{m.Text}\n{baseUrl}{m.Link}";
            try
            {
                switch (m.Channel)
                {
                    case Channel.Sms:
                        var phone = await db.Users.Where(u => u.Id == m.UserId && u.IsActive).Select(u => u.Phone).FirstOrDefaultAsync(ct);
                        if (phone is null) { m.Skip("no active user"); continue; }
                        await sms.SendTextAsync(phone, text, ct);
                        break;
                    case Channel.Bale or Channel.Telegram:
                        var kind = m.Channel == Channel.Bale ? Messenger.Bale : Messenger.Telegram;
                        var link = await db.MessengerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == m.UserId && l.Messenger == kind, ct);
                        if (link is null) { m.Skip("not linked"); continue; }
                        await messengers.SendAsync(kind, link.ChatId, text, ct);
                        break;
                    default:
                        m.Skip("channel not supported");
                        continue;
                }
                m.Sent(clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never log the text (it can contain names) — only the channel and the error type.
                logger.LogWarning("Outbox {Id} via {Channel} failed: {Error}", m.Id, m.Channel, ex.GetType().Name);
                m.Fail(ex.GetType().Name, clock.GetUtcNow());
            }
        }
        await db.SaveChangesAsync(ct);
        return batch.Count;
    }
}
