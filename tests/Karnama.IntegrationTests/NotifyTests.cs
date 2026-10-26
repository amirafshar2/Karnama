using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karnama.Domain.Catalog;
using Karnama.Domain.Notify;
using Karnama.Infrastructure.Notify;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Karnama.IntegrationTests;

/// <summary>M6: inbox, client ↔ agency messages, bot linking (Bale webhook, signed Telegram gateway), outbox, publishing reminders.</summary>
public sealed class NotifyTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return r.StatusCode == HttpStatusCode.NoContent ? default : await r.JsonAsync();
    }

    private async Task<(Guid ClientId, Guid OwnPlatform, string OwnHandle, Guid AgencyPlatform, string AgencyHandle, Guid OtherClient)> CafeAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cafe = await db.Clients.AsNoTracking().FirstAsync(c => c.BusinessName == "کافه نمونه");
        var other = await db.Clients.AsNoTracking().FirstAsync(c => c.BusinessName == "پوشاک نمونه");
        var own = cafe.Accounts.First(a => a.Management == PageManagement.ClientManaged);
        var agency = cafe.Accounts.First(a => a.Management == PageManagement.AgencyManaged);
        return (cafe.Id, own.PlatformId, own.Handle, agency.PlatformId, agency.Handle, other.Id);
    }

    private async Task ShiftAsync(Guid id, TimeSpan by)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.PublishJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.PublishAt, j => j.PublishAt + by));
    }

    private async Task TickAsync()
    {
        using var scope = _f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Karnama.Application.Publishing.PublishService>().TickAsync(default);
    }

    [Fact]
    public async Task Client_message_reaches_admins_and_the_reply_reaches_the_client()
    {
        var client = await As("client");
        var admin = await As("admin");
        var editor = await As("editor");
        var orderId = (await Ok(await client.GetAsync("/api/v1/portal/home"))).GetProperty("orders")[0].GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync($"/api/v1/portal/orders/{orderId}/messages", new { body = "  " })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var sent = await Ok(await client.PostAsJsonAsync($"/api/v1/portal/orders/{orderId}/messages", new { body = "لطفاً رنگ لوگو پررنگ‌تر باشد" }));
        sent.GetProperty("fromMe").GetBoolean().Should().BeTrue();

        var inbox = await Ok(await admin.GetAsync("/api/v1/notifications"));
        inbox.GetProperty("unread").GetInt32().Should().BeGreaterThan(0);
        var n = inbox.GetProperty("items").EnumerateArray().First(i => i.GetProperty("kind").GetString() == "portal.message");
        n.GetProperty("body").GetString().Should().Contain("رنگ لوگو");
        // Workers don't get client messages (only admins talk to clients).
        (await Ok(await editor.GetAsync("/api/v1/notifications"))).GetProperty("items").EnumerateArray()
            .Should().NotContain(i => i.GetProperty("kind").GetString() == "portal.message");

        await Ok(await admin.PostAsJsonAsync("/api/v1/notifications/read", new { id = (Guid?)null }));
        (await Ok(await admin.GetAsync("/api/v1/notifications"))).GetProperty("unread").GetInt32().Should().Be(0);

        // The admin replies on the work card (client-visible) → the client's bell.
        var link = n.GetProperty("link").GetString()!;
        var workId = Guid.Parse(link[(link.IndexOf('=') + 1)..]);
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{workId}/comments", new { body = "حتماً، نسخه‌ی جدید را می‌فرستیم.", visibility = "client" }));
        var mine = await Ok(await client.GetAsync("/api/v1/notifications"));
        mine.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("kind").GetString() == "portal.message");
        var thread = await Ok(await client.GetAsync($"/api/v1/portal/orders/{orderId}/messages"));
        thread.EnumerateArray().Select(m => m.GetProperty("author").GetString()).Should().Contain("آژانس");

        // Another client's order: not found.
        var other = (await Ok(await admin.GetAsync("/api/v1/orders?limit=100"))).GetProperty("items").EnumerateArray()
            .First(o => o.GetProperty("clientName").GetString() != "کافه نمونه").GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/v1/portal/orders/{other}/messages", new { body = "سلام" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync($"/api/v1/portal/orders/{other}/messages", new { body = "سلام" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Bale_links_with_a_one_time_code_and_important_notifications_go_through_the_outbox()
    {
        var editor = await As("editor");
        var code = await Ok(await editor.PostAsync("/api/v1/notifications/links/bale", null));
        var url = code.GetProperty("url").GetString()!;
        url.Should().StartWith("https://ble.ir/karnama_test_bot?start=");
        var secret = url[(url.IndexOf('=') + 1)..];
        (await editor.PostAsync("/api/v1/notifications/links/telegram", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // no bot configured

        var anon = _f.CreateClient();
        object Update(long id, string text) => new { update_id = id, message = new { chat = new { id = 777001L }, text } };
        (await anon.PostAsJsonAsync("/api/v1/bots/bale/wrong-secret", Update(1, "/start " + secret))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.PostAsJsonAsync("/api/v1/bots/bale/" + KarnamaFactory.BaleSecret, Update(9001, "/start nope"))).StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Messenger.Sent.Should().Contain(m => m.ChatId == 777001 && m.Text.Contains("معتبر نیست"));
        (await anon.PostAsJsonAsync("/api/v1/bots/bale/" + KarnamaFactory.BaleSecret, Update(9002, "/start " + secret))).StatusCode.Should().Be(HttpStatusCode.OK);
        var prefs = await Ok(await editor.GetAsync("/api/v1/notifications/prefs"));
        prefs.GetProperty("linked").EnumerateArray().Select(x => x.GetString()).Should().Contain("bale");

        // The code is single-use; a replayed update id is ignored.
        var before = _f.Messenger.Sent.Count;
        await anon.PostAsJsonAsync("/api/v1/bots/bale/" + KarnamaFactory.BaleSecret, Update(9002, "/start " + secret));
        _f.Messenger.Sent.Count.Should().Be(before);
        await anon.PostAsJsonAsync("/api/v1/bots/bale/" + KarnamaFactory.BaleSecret, Update(9003, "/start " + secret));
        _f.Messenger.Sent.Last().Text.Should().Contain("معتبر نیست");

        // Opt in to Bale, then an important notification is queued for it and the dispatcher sends it (with the app link).
        await Ok(await editor.PutAsJsonAsync("/api/v1/notifications/prefs", new { channels = new[] { "inApp", "bale" }, muted = Array.Empty<string>() }));
        using (var scope = _f.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<Karnama.Application.Notify.Notifier>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var editorId = await db.MessengerLinks.Where(l => l.ChatId == 777001).Select(l => l.UserId).FirstAsync();
            await notifier.NotifyAsync([editorId], "test.important", "کار فوری", "متن آزمایشی", "/work", true, "test-important-1", default);
            await notifier.NotifyAsync([editorId], "test.important", "کار فوری", "متن آزمایشی", "/work", true, "test-important-1", default);   // same event: once
            await db.SaveChangesAsync();
            (await db.Outbox.CountAsync(o => o.UserId == editorId && o.IdempotencyKey.StartsWith("test-important-1"))).Should().Be(1);
        }
        var dispatcher = new OutboxDispatcher(_f.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(default);
        _f.Messenger.Sent.Should().Contain(m => m.M == Messenger.Bale && m.ChatId == 777001 && m.Text.Contains("کار فوری") && m.Text.Contains("/work"));
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Outbox.Where(o => o.IdempotencyKey.StartsWith("test-important-1")).Select(o => o.Status).FirstAsync()).Should().Be(OutboxStatus.Sent);
        }

        // /stop from the chat unlinks it.
        await anon.PostAsJsonAsync("/api/v1/bots/bale/" + KarnamaFactory.BaleSecret, Update(9004, "/stop"));
        (await Ok(await editor.GetAsync("/api/v1/notifications/prefs"))).GetProperty("linked").EnumerateArray().Select(x => x.GetString()).Should().NotContain("bale");
    }

    [Fact]
    public async Task Telegram_gateway_updates_must_be_signed()
    {
        var anon = _f.CreateClient();
        var body = JsonSerializer.Serialize(new { updateId = 5, chatId = 4242L, text = "/start abc" });
        HttpRequestMessage Req(string sig, long ts)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bots/telegram") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            r.Headers.Add(GatewaySignature.TimestampHeader, ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            r.Headers.Add(GatewaySignature.SignatureHeader, sig);
            return r;
        }
        var now = _f.Clock.GetUtcNow().ToUnixTimeSeconds();
        (await anon.SendAsync(Req("00", now))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.SendAsync(Req(GatewaySignature.Compute(KarnamaFactory.GatewaySecret, now - 3600, body), now - 3600))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);  // replay
        (await anon.SendAsync(Req(GatewaySignature.Compute(KarnamaFactory.GatewaySecret, now, body), now))).StatusCode.Should().Be(HttpStatusCode.OK);
        _f.Messenger.Sent.Should().Contain(m => m.M == Messenger.Telegram && m.ChatId == 4242);
    }

    [Fact]
    public async Task Publish_reminder_goes_to_the_client_for_its_own_page_and_overdue_to_managers()
    {
        var admin = await As("admin");
        var (clientId, ownPlatform, ownHandle, agencyPlatform, agencyHandle, otherClient) = await CafeAsync();
        var at = _f.Clock.GetUtcNow().AddMinutes(10);
        object Job(Guid platform, string handle, Guid? client = null) => new
        {
            clientId = client ?? clientId, platformId = platform, handle, publishAt = at, caption = "پست آزمایشی", hashtags = new[] { "#کافه", "شیراز" }, fileIds = Array.Empty<Guid>(),
        };
        (await admin.PostAsJsonAsync("/api/v1/publishing", Job(ownPlatform, ownHandle, otherClient))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var own = await Ok(await admin.PostAsJsonAsync("/api/v1/publishing", Job(ownPlatform, ownHandle)));
        own.GetProperty("management").GetString().Should().Be("clientManaged");
        own.GetProperty("hashtags").EnumerateArray().Select(h => h.GetString()).Should().Equal("کافه", "شیراز");
        var agency = await Ok(await admin.PostAsJsonAsync("/api/v1/publishing", Job(agencyPlatform, agencyHandle)));

        var editor = await As("editor");
        (await editor.PostAsJsonAsync("/api/v1/publishing", Job(agencyPlatform, agencyHandle))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var cal = await Ok(await admin.GetAsync($"/api/v1/publishing?from={Uri.EscapeDataString(at.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(at.AddDays(1).ToString("O"))}"));
        cal.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Contain([own.GetProperty("id").GetGuid(), agency.GetProperty("id").GetGuid()]);

        // Time passes: move the posts into the past instead of the shared clock (tokens are checked against real time).
        await ShiftAsync(own.GetProperty("id").GetGuid(), TimeSpan.FromMinutes(-11));
        await ShiftAsync(agency.GetProperty("id").GetGuid(), TimeSpan.FromMinutes(-11));
        await TickAsync();
        var client = await As("client");
        var portal = await Ok(await client.GetAsync("/api/v1/portal/publishing"));
        var mine = portal.EnumerateArray().First(j => j.GetProperty("id").GetGuid() == own.GetProperty("id").GetGuid());
        mine.GetProperty("status").GetString().Should().Be("due");
        (await Ok(await client.GetAsync("/api/v1/notifications"))).GetProperty("items").EnumerateArray()
            .Should().Contain(i => i.GetProperty("kind").GetString() == "publish.reminder");

        // The client may confirm only its own page; the link must be https.
        (await client.PostAsJsonAsync($"/api/v1/portal/publishing/{agency.GetProperty("id").GetGuid()}/published", new { postUrl = (string?)null }))
            .IsSuccessStatusCode.Should().BeFalse();
        (await client.PostAsJsonAsync($"/api/v1/portal/publishing/{own.GetProperty("id").GetGuid()}/published", new { postUrl = "javascript:alert(1)" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var done = await Ok(await client.PostAsJsonAsync($"/api/v1/portal/publishing/{own.GetProperty("id").GetGuid()}/published", new { postUrl = "https://t.me/cafe_nemoone/12" }));
        done.GetProperty("status").GetString().Should().Be("published");
        done.GetProperty("publishedByClient").GetBoolean().Should().BeTrue();

        // Two hours later the agency post is overdue and the managers hear about it.
        await ShiftAsync(agency.GetProperty("id").GetGuid(), TimeSpan.FromHours(-2));
        await TickAsync();
        var admin2 = admin;
        var after = await Ok(await admin2.GetAsync($"/api/v1/publishing?from={Uri.EscapeDataString(at.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(at.AddDays(1).ToString("O"))}"));
        after.EnumerateArray().First(j => j.GetProperty("id").GetGuid() == agency.GetProperty("id").GetGuid()).GetProperty("status").GetString().Should().Be("overdue");
        (await Ok(await admin2.GetAsync("/api/v1/notifications"))).GetProperty("items").EnumerateArray()
            .Should().Contain(i => i.GetProperty("kind").GetString() == "publish.overdue");
    }

    [Fact]
    public async Task Only_a_worker_on_the_order_may_mark_an_agency_post_published()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var writer = await As("writer");
        var (clientId, _, _, agencyPlatform, agencyHandle, _) = await CafeAsync();
        var services = await Ok(await admin.GetAsync("/api/v1/services/options"));
        var post = services.EnumerateArray().First(s => s.GetProperty("name").GetString() == "پست تکی").GetProperty("id").GetGuid();
        var order = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", new
        {
            clientId, serviceTypeId = post, title = "پست انتشار", answers = new Dictionary<string, object> { ["topic"] = "انتشار", ["mood"] = "friendly" }, autoAccept = true,
        }));
        var orderId = order.GetProperty("id").GetGuid();
        var itemId = order.GetProperty("stages")[0].GetProperty("id").GetGuid();
        var users = await Ok(await admin.GetAsync("/api/v1/users?limit=100"));
        var editorId = users.GetProperty("items").EnumerateArray().First(u => u.GetProperty("displayName").GetString()!.StartsWith("علی")).GetProperty("id").GetGuid();
        var v = (await Ok(await admin.GetAsync($"/api/v1/work/{itemId}"))).GetProperty("card").GetProperty("version").GetUInt32();
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/assign", new { userId = editorId, version = v }));

        object Job(Guid? oid) => new
        {
            clientId, orderId = oid, platformId = agencyPlatform, handle = agencyHandle, publishAt = _f.Clock.GetUtcNow().AddMinutes(30), caption = "پست", fileIds = Array.Empty<Guid>(),
        };
        var withOrder = (await Ok(await admin.PostAsJsonAsync("/api/v1/publishing", Job(orderId)))).GetProperty("id").GetGuid();
        var noOrder = (await Ok(await admin.PostAsJsonAsync("/api/v1/publishing", Job(null)))).GetProperty("id").GetGuid();

        // Not on the order (the writer) → refused; a job without an order → only managers; the assigned editor → OK.
        (await writer.PostAsJsonAsync($"/api/v1/publishing/{withOrder}/published", new { postUrl = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.PostAsJsonAsync($"/api/v1/publishing/{noOrder}/published", new { postUrl = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Ok(await editor.PostAsJsonAsync($"/api/v1/publishing/{withOrder}/published", new { postUrl = "https://instagram.com/p/abc" }))).GetProperty("status").GetString().Should().Be("published");
        (await Ok(await admin.PostAsJsonAsync($"/api/v1/publishing/{noOrder}/published", new { postUrl = (string?)null }))).GetProperty("status").GetString().Should().Be("published");
    }
}
