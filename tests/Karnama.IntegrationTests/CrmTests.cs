using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karnama.Domain.Crm;
using Karnama.Infrastructure.Notify;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N7: CRM (PROMPT2 §4 / §15): marketers see only their leads (404), duplicates, round-robin, calls, conversion, inbound, follow-ups, report.</summary>
public sealed class CrmTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return r.StatusCode == HttpStatusCode.NoContent || r.Content.Headers.ContentLength == 0 ? default : await r.JsonAsync();
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<Guid> UserAsync(string phone) => Db(db => db.Users.Where(u => u.Phone == phone).Select(u => u.Id).FirstAsync());

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<KarnamaFactory, (string Token, Guid Id)> Second = new();

    /// <summary>A second marketer with the same role as the demo one, logged in once per test database.</summary>
    private async Task<(HttpClient Client, Guid Id)> SecondMarketerAsync()
    {
        if (Second.TryGetValue(_f, out var cached)) return (_f.CreateClient().WithToken(cached.Token), cached.Id);
        var phone = "09351230099";
        var id = await Db(async db =>
        {
            if (await db.Users.FirstOrDefaultAsync(u => u.Phone == phone) is { } existing) return existing.Id;
            var role = (await db.Roles.ToListAsync()).First(r => r.Name.Get("fa") == "بازاریاب").Id;
            var user = Karnama.Domain.Identity.User.Create(phone, "بازاریاب دوم", Karnama.Domain.Identity.UserKind.Staff).Value!;
            user.AssignRole(role);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });
        var c = _f.CreateClient();
        var req = await Ok(await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone }));
        var code = await _f.Sms.WaitForCodeAsync(phone);
        var login = await Ok(await c.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId = req.GetProperty("challengeId").GetGuid(), code }));
        var token = login.GetProperty("session").GetProperty("accessToken").GetString()!;
        Second[_f] = (token, id);
        return (c.WithToken(token), id);
    }

    [Fact]
    public async Task A_marketer_sees_only_own_leads_and_another_marketers_lead_is_404()
    {
        var marketer = await As("marketer");
        var mine = await Ok(await marketer.PostAsJsonAsync("/api/v1/crm/leads", new { name = "سرنخ من", phone = "09381110001", business = "کافی‌شاپ" }));
        var myId = mine.GetProperty("lead").GetProperty("id").GetGuid();
        mine.GetProperty("lead").GetProperty("ownerId").GetGuid().Should().Be(await UserAsync("09000000008"), "a marketer's new lead is theirs");

        var (other, _) = await SecondMarketerAsync();
        (await other.GetAsync($"/api/v1/crm/leads/{myId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/crm/leads/{myId}", new { name = "x", phone = "09381110001" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/v1/crm/leads/{myId}/calls", new { direction = "outgoing", durationSeconds = 10, outcome = "interested" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/v1/crm/leads/{myId}/convert", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var board = await Ok(await other.GetAsync("/api/v1/crm/board"));
        board.EnumerateArray().Should().NotContain(l => l.GetProperty("id").GetGuid() == myId);

        // Same phone by the other marketer: refused without revealing whose it is.
        var dup = await other.PostAsJsonAsync("/api/v1/crm/leads", new { name = "تکراری", phone = "0938 111 0001" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await dup.Content.ReadAsStringAsync()).Should().NotContain("سرنخ من");

        // Workers and clients have no CRM at all.
        (await (await As("editor")).GetAsync("/api/v1/crm/board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await As("client")).GetAsync("/api/v1/crm/board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // A marketer can't hand leads out.
        (await marketer.PostAsJsonAsync($"/api/v1/crm/leads/{myId}/assign", new { ownerId = (Guid?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Round_robin_hands_out_in_turn_and_transfers_are_logged()
    {
        var admin = await As("admin");
        var (_, second) = await SecondMarketerAsync();
        var first = await UserAsync("09000000008");
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
            ids.Add((await Ok(await admin.PostAsJsonAsync("/api/v1/crm/leads", new { name = $"چرخشی {i}", phone = $"0938222000{i}", ownerId = (Guid?)null }))).GetProperty("lead").GetProperty("id").GetGuid());
        (await Ok(await admin.PostAsJsonAsync("/api/v1/crm/round-robin", new { leadIds = ids }))).GetProperty("assigned").GetInt32().Should().Be(4);
        var owners = await Db(db => db.Leads.Where(l => ids.Contains(l.Id)).OrderBy(l => l.CreatedAt).Select(l => l.OwnerId!.Value).ToListAsync());
        owners.Distinct().Should().BeEquivalentTo(new[] { first, second });
        owners.Count(o => o == first).Should().Be(2, "in turn");

        // Transfer → history.
        var detail = await Ok(await admin.PostAsJsonAsync($"/api/v1/crm/leads/{ids[0]}/assign", new { ownerId = owners[0] == first ? second : first }));
        detail.GetProperty("activities").EnumerateArray().Count(a => a.GetProperty("kind").GetString() == "assigned").Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Call_result_follow_up_reminder_once_and_conversion_to_client()
    {
        var marketer = await As("marketer");
        var lead = await Ok(await marketer.PostAsJsonAsync("/api/v1/crm/leads", new { name = "آقای تبدیلی", phone = "09383330001", business = "نانوایی تست", instagram = "@bakery_test" }));
        var id = lead.GetProperty("lead").GetProperty("id").GetGuid();
        var newStage = lead.GetProperty("lead").GetProperty("stageId").GetGuid();

        var due = _f.Clock.GetUtcNow().AddMinutes(-1);
        var called = await Ok(await marketer.PostAsJsonAsync($"/api/v1/crm/leads/{id}/calls", new { direction = "outgoing", durationSeconds = 180, outcome = "later", note = "عصر تماس بگیرید", nextFollowUpAt = due }));
        called.GetProperty("lead").GetProperty("stageId").GetGuid().Should().NotBe(newStage, "an answered call moves a new lead forward");
        called.GetProperty("activities").EnumerateArray().Should().Contain(a => a.GetProperty("kind").GetString() == "call" && a.GetProperty("durationSeconds").GetInt32() == 180);
        (await Ok(await marketer.GetAsync("/api/v1/crm/today"))).EnumerateArray().Should().Contain(l => l.GetProperty("id").GetGuid() == id);

        async Task Remind()
        {
            using var scope = _f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Karnama.Application.Crm.CrmService>().RemindFollowUpsAsync(default);
        }
        await Remind();
        await Remind();
        (await Db(db => db.Notifications.CountAsync(n => n.Kind == "crm.followUp" && n.EventKey.StartsWith($"crm-follow:{id:N}")))).Should().Be(1);

        // Moving to "lost" needs a reason.
        var stages = await Ok(await marketer.GetAsync("/api/v1/crm/stages"));
        var lost = stages.EnumerateArray().First(s => s.GetProperty("kind").GetString() == "lost").GetProperty("id").GetGuid();
        (await marketer.PostAsJsonAsync($"/api/v1/crm/leads/{id}/move", new { stageId = lost, lostReason = (string?)null })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var converted = await Ok(await marketer.PostAsync($"/api/v1/crm/leads/{id}/convert", null));
        var clientId = converted.GetProperty("clientId").GetGuid();
        var client = await Db(db => db.Clients.AsNoTracking().FirstAsync(c => c.Id == clientId));
        client.BusinessName.Should().Be("نانوایی تست");
        client.Mobile.Should().Be("09383330001");
        client.Accounts.Should().Contain(a => a.Handle == "bakery_test");
        var after = await Ok(await marketer.GetAsync($"/api/v1/crm/leads/{id}"));
        var won = stages.EnumerateArray().First(s => s.GetProperty("kind").GetString() == "won").GetProperty("id").GetGuid();
        after.GetProperty("lead").GetProperty("stageId").GetGuid().Should().Be(won);
        after.GetProperty("lead").GetProperty("clientId").GetGuid().Should().Be(clientId);
        (await Ok(await marketer.PostAsync($"/api/v1/crm/leads/{id}/convert", null))).GetProperty("clientId").GetGuid().Should().Be(clientId, "converting twice gives the same client");

        var report = await Ok(await marketer.GetAsync($"/api/v1/crm/report?from={DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}"));
        var row = report.GetProperty("marketers").EnumerateArray().Single();
        row.GetProperty("calls").GetInt32().Should().BeGreaterThan(0);
        row.GetProperty("won").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Linking_a_lead_to_an_existing_client_earns_the_marketer_nothing_and_reveals_nothing()
    {
        var marketer = await As("marketer");
        var range = $"from={DateTime.UtcNow.AddDays(-360):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}";
        long Sales(JsonElement r) => r.GetProperty("marketers").EnumerateArray().Single().GetProperty("sales").GetInt64();
        var before = Sales(await Ok(await marketer.GetAsync($"/api/v1/crm/report?{range}")));
        // The demo café (09000000004) already has a paid package.
        var lead = await Ok(await marketer.PostAsJsonAsync("/api/v1/crm/leads", new { name = "کافه را می‌شناسم", phone = "09000000004" }));
        var id = lead.GetProperty("lead").GetProperty("id").GetGuid();
        var converted = await Ok(await marketer.PostAsync($"/api/v1/crm/leads/{id}/convert", null));
        converted.GetProperty("existing").GetBoolean().Should().BeFalse("a marketer must not learn that the number is already a client");
        Sales(await Ok(await marketer.GetAsync($"/api/v1/crm/report?{range}"))).Should().Be(before);
    }

    [Fact]
    public async Task Website_form_and_bot_messages_become_leads_without_duplicates()
    {
        var anon = _f.CreateClient();
        await Ok(await anon.PostAsJsonAsync("/api/v1/public/site/leads", new { name = "فرم‌پرکن", phone = "09384440001", message = "پکیج طلایی", source = "/plans" }));
        var lead = await Db(db => db.Leads.AsNoTracking().FirstAsync(l => l.Phone == "09384440001"));
        lead.Origin.Should().Be("/plans");

        // A stranger writes to the Telegram bot: a lead; their number in a later message joins the same lead.
        async Task Bot(long updateId, string text)
        {
            var body = JsonSerializer.Serialize(new { update_id = updateId, message = new { message_id = 1, chat = new { id = 777_888L, type = "private" }, from = new { id = 777_888L, first_name = "مریم", last_name = "تلگرامی" }, text } });
            var now = _f.Clock.GetUtcNow().ToUnixTimeSeconds();
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bots/telegram") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            r.Headers.Add(GatewaySignature.TimestampHeader, now.ToString(System.Globalization.CultureInfo.InvariantCulture));
            r.Headers.Add(GatewaySignature.SignatureHeader, GatewaySignature.Compute(KarnamaFactory.GatewaySecret, now, body));
            (await anon.SendAsync(r)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        await Bot(880_001, "سلام، قیمت مدیریت پیج چند است؟");
        await Bot(880_002, "شماره‌ام ۰۹۳۸۴۴۴۰۰۰۲ است");
        var fromBot = await Db(db => db.Leads.AsNoTracking().Where(l => l.MessengerChatId == 777_888).ToListAsync());
        fromBot.Should().HaveCount(1);
        fromBot[0].Name.Should().Be("مریم تلگرامی");
        fromBot[0].Phone.Should().Be("09384440002");
        _f.Messenger.Sent.Should().Contain(m => m.ChatId == 777_888 && m.Text.Contains("پیامتان رسید"));
    }
}
