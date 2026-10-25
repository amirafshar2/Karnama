using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Karnama.TelegramGateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N5: the Telegram gateway outside Iran (PROMPT2 §8.1 / §8.4 / §15).</summary>
public sealed class GatewayTests : IDisposable
{
    private const string Secret = "gateway-test-secret-0123456789-abcdefgh";
    private const string WebhookSecret = "tg-secret-token-0123456789";
    private const string WebhookPath = "hook-path-0123456789abcdef";
    private readonly string _queue = Path.Combine(Path.GetTempPath(), "karnama-gw-" + Guid.NewGuid().ToString("N"));
    private readonly Stub _telegram = new("""{"ok":true,"result":{"message_id":42}}""");
    private readonly Stub _iran = new("");
    private readonly WebApplicationFactory<GatewayEntry> _gw;

    public GatewayTests()
    {
        _gw = new WebApplicationFactory<GatewayEntry>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Telegram:BotToken", "123456:TEST-token-not-real-0123456789");
            b.UseSetting("Telegram:WebhookSecret", WebhookSecret);
            b.UseSetting("Telegram:WebhookPath", WebhookPath);
            b.UseSetting("Gateway:Secret", Secret);
            b.UseSetting("Gateway:IranApiUrl", "https://iran.test");
            b.UseSetting("Gateway:QueuePath", _queue);
            b.ConfigureServices(s =>
            {
                s.AddHttpClient("telegram").ConfigurePrimaryHttpMessageHandler(() => _telegram);
                s.AddHttpClient("iran").ConfigurePrimaryHttpMessageHandler(() => _iran);
            });
        });
    }

    public void Dispose()
    {
        _gw.Dispose();
        try { Directory.Delete(_queue, true); } catch (IOException) { }
    }

    private sealed class Stub(string answer) : HttpMessageHandler
    {
        public ConcurrentQueue<(string Path, string Body, string? Signature)> Requests { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Enqueue((request.RequestUri!.AbsolutePath, body, request.Headers.TryGetValues(Signature.SignatureHeader, out var v) ? v.First() : null));
            return new HttpResponseMessage(Status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpRequestMessage Signed(string path, string body, long? at = null, string? signature = null)
    {
        var ts = at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var r = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        r.Headers.Add(Signature.TimestampHeader, ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        r.Headers.Add(Signature.SignatureHeader, signature ?? Signature.Compute(Secret, ts, body));
        return r;
    }

    [Fact]
    public async Task Calls_from_Iran_must_be_signed_fresh_and_never_replayed()
    {
        var c = _gw.CreateClient();
        const string body = """{"method":"copyMessage","args":{"chat_id":1,"from_chat_id":2,"message_id":3}}""";
        (await c.SendAsync(Signed("/v1/call", body, signature: "00"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        (await c.SendAsync(Signed("/v1/call", body, old))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "old timestamp");
        var unsigned = new HttpRequestMessage(HttpMethod.Post, "/v1/call") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        (await c.SendAsync(unsigned)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ok = await c.SendAsync(Signed("/v1/call", body, ts));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Contain("\"message_id\":42");
        _telegram.Requests.Should().ContainSingle(r => r.Path.EndsWith("/copyMessage"));
        (await c.SendAsync(Signed("/v1/call", body, ts))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the same signed request twice = replay");

        // Only the allowed Bot API methods.
        (await c.SendAsync(Signed("/v1/call", """{"method":"deleteWebhook","args":{}}"""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_needs_the_secret_and_updates_are_queued_and_forwarded_signed_once_per_update_id()
    {
        var c = _gw.CreateClient();
        const string update = """{"update_id":5001,"message":{"message_id":1,"chat":{"id":77,"type":"private"},"text":"/start x"}}""";
        HttpRequestMessage Hook(string path, string? token)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, $"/tg/{path}") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
            if (token is not null) r.Headers.Add("X-Telegram-Bot-Api-Secret-Token", token);
            return r;
        }
        (await c.SendAsync(Hook("wrong-path", WebhookSecret))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.SendAsync(Hook(WebhookPath, "wrong-token"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.SendAsync(Hook(WebhookPath, null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.SendAsync(Hook(WebhookPath, WebhookSecret))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.SendAsync(Hook(WebhookPath, WebhookSecret))).StatusCode.Should().Be(HttpStatusCode.OK);   // Telegram re-sends

        var queue = _gw.Services.GetRequiredService<UpdateQueue>();
        queue.Count.Should().Be(1, "same update_id = one file");

        // Iran down → kept; Iran up → delivered with a valid signature, then removed.
        var forwarder = _gw.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Forwarder>().Single();
        _iran.Status = HttpStatusCode.ServiceUnavailable;
        (await forwarder.ForwardOnceAsync(default)).Should().Be(-1);
        queue.Count.Should().Be(1);
        _iran.Status = HttpStatusCode.OK;
        // The background loop may have sent it already; either way it reaches Iran signed and leaves the queue.
        await forwarder.ForwardOnceAsync(default);
        queue.Count.Should().Be(0);
        var sent = _iran.Requests.Last(r => r.Path == "/api/v1/bots/telegram");
        sent.Body.Should().Be(update);
        sent.Signature.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Gateway_and_api_sign_the_same_way()
    {
        Signature.Compute(Secret, 1_700_000_000, "{}").Should().Be(Karnama.Infrastructure.Notify.GatewaySignature.Compute(Secret, 1_700_000_000, "{}"));
    }
}
