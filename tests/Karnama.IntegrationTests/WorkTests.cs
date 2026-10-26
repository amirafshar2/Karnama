using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Karnama.IntegrationTests;

/// <summary>M3: orders → work items → board moves (who may do what), quota, comments, resumable uploads, change cursor.</summary>
public sealed class WorkTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    private async Task<(Guid ClientId, Guid ReelId, Guid EditorId)> RefsAsync(HttpClient admin, string client = "کافه نمونه")
    {
        var clients = await Ok(await admin.GetAsync("/api/v1/clients?q=" + Uri.EscapeDataString(client)));
        var clientId = clients.GetProperty("items")[0].GetProperty("id").GetGuid();
        var services = await Ok(await admin.GetAsync("/api/v1/services/options"));
        var reel = services.EnumerateArray().First(s => s.GetProperty("name").GetString() == "ریلز").GetProperty("id").GetGuid();
        var users = await Ok(await admin.GetAsync("/api/v1/users?limit=100"));
        var editor = users.GetProperty("items").EnumerateArray().First(u => u.GetProperty("displayName").GetString()!.StartsWith("علی")).GetProperty("id").GetGuid();
        return (clientId, reel, editor);
    }

    private static object Order(Guid clientId, Guid serviceId, bool autoAccept = true, string? topic = "معرفی منوی پاییز") => new
    {
        clientId, serviceTypeId = serviceId, title = "ریلز منوی پاییز", answers = topic is null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["topic"] = topic, ["mood"] = "friendly" },
        priority = "high", autoAccept,
    };

    [Fact]
    public async Task Order_creates_work_items_uses_quota_and_validates_the_form()
    {
        var admin = await As("admin");
        // The clinic's sample package has 4 reels and the demo board already used 1 (only this test orders for it).
        var (clientId, reel, _) = await RefsAsync(admin, "کلینیک زیبایی نمونه");

        var bad = await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel, topic: null));
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("موضوع محتوا");

        var o = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        o.GetProperty("status").GetString().Should().Be("inProgress");
        o.GetProperty("outsidePackage").GetBoolean().Should().BeFalse();
        o.GetProperty("number").GetInt64().Should().BeGreaterThan(1000);
        var stages = o.GetProperty("stages").EnumerateArray().ToList();
        stages.Should().HaveCount(3);
        stages[0].GetProperty("status").GetString().Should().Be("todo");
        stages[1].GetProperty("status").GetString().Should().Be("waiting");
        o.GetProperty("answers").EnumerateArray().First(a => a.GetProperty("key").GetString() == "topic").GetProperty("value").GetString().Should().Be("معرفی منوی پاییز");

        // A submitted (not auto-accepted) order waits; accept needs the current version.
        var pending = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel, autoAccept: false)));
        pending.GetProperty("status").GetString().Should().Be("submitted");
        pending.GetProperty("stages").GetArrayLength().Should().Be(0);
        var accepted = await Ok(await admin.PostAsync($"/api/v1/orders/{pending.GetProperty("id").GetGuid()}/accept?version={pending.GetProperty("version").GetUInt32()}", null));
        accepted.GetProperty("stages").GetArrayLength().Should().Be(3);
        (await admin.PostAsync($"/api/v1/orders/{pending.GetProperty("id").GetGuid()}/accept?version={accepted.GetProperty("version").GetUInt32()}", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Quota: the 4th reel still fits, the 5th is extra (billed at the service's base price).
        (await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)))).GetProperty("outsidePackage").GetBoolean().Should().BeFalse();
        var extra = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        extra.GetProperty("outsidePackage").GetBoolean().Should().BeTrue();
        extra.GetProperty("price").GetInt64().Should().Be(25_000_000);

        // PROMPT §6.4: the admins are warned about the extra order (once per order).
        var bell = (await Ok(await admin.GetAsync("/api/v1/notifications"))).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("kind").GetString() == "orders.outsidePackage").ToList();
        bell.Should().ContainSingle(i => i.GetProperty("title").GetString()!.Contains(extra.GetProperty("number").GetInt64().ToString()));
    }

    [Fact]
    public async Task Work_goes_only_to_people_who_can_run_it()
    {
        var admin = await As("admin");
        var (clientId, reel, editorId) = await RefsAsync(admin);
        var o = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        var itemId = o.GetProperty("stages")[0].GetProperty("id").GetGuid();
        var users = (await Ok(await admin.GetAsync("/api/v1/users?limit=100"))).GetProperty("items").EnumerateArray().ToList();
        var marketer = users.First(u => u.GetProperty("displayName").GetString()!.Contains("بازاریاب")).GetProperty("id").GetGuid();

        var suggestions = (await Ok(await admin.GetAsync($"/api/v1/work/{itemId}/suggestions"))).EnumerateArray().Select(s => s.GetProperty("userId").GetGuid()).ToList();
        suggestions.Should().Contain(editorId).And.NotContain(marketer);

        var v = (await Ok(await admin.GetAsync($"/api/v1/work/{itemId}"))).GetProperty("card").GetProperty("version").GetUInt32();
        var bad = await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/assign", new { userId = marketer, version = v });
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("نمی‌تواند کار انجام دهد");
        (await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/assign", new { userId = editorId, version = v }))).GetProperty("assigneeId").GetGuid().Should().Be(editorId);
    }

    /// <summary>Moves every stage deadline of an order into the past, as if the plan's time had run out.</summary>
    private async Task ShiftDeadlinesAsync(Guid orderId, TimeSpan by)
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Karnama.Infrastructure.Persistence.AppDbContext>();
        await db.WorkItems.Where(w => w.OrderId == orderId).ExecuteUpdateAsync(
            s => s.SetProperty(w => w.Deadline, w => w.Deadline + by));
    }

    [Fact]
    public async Task A_stage_that_unlocks_late_gets_its_full_window_back()
    {
        var admin = await As("admin");
        var (clientId, reel, _) = await RefsAsync(admin);
        var o = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        var ids = o.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        async Task<JsonElement> Card(Guid id) => (await Ok(await admin.GetAsync($"/api/v1/work/{id}"))).GetProperty("card");
        var plan2 = (await Card(ids[1])).GetProperty("deadline").GetDateTimeOffset();
        var plan1 = (await Card(ids[0])).GetProperty("deadline").GetDateTimeOffset();
        var window = plan2 - plan1;   // what the plan gave stage 2

        // 100 hours later stage 1 is finally done: stage 2's planned deadline has long passed.
        await ShiftDeadlinesAsync(o.GetProperty("id").GetGuid(), TimeSpan.FromHours(-100));
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{ids[0]}/move", new { status = "done", version = (await Card(ids[0])).GetProperty("version").GetUInt32() }));
        var now = _f.Clock.GetUtcNow();
        var c2 = await Card(ids[1]);
        c2.GetProperty("status").GetString().Should().Be("todo");
        c2.GetProperty("deadline").GetDateTimeOffset().Should().BeCloseTo(now + window, TimeSpan.FromMinutes(1));
        // Stage 3 is still waiting: it moves only when it unlocks.
        (await Card(ids[2])).GetProperty("deadline").GetDateTimeOffset().Should().BeCloseTo(o.GetProperty("stages")[2].GetProperty("deadline").GetDateTimeOffset() - TimeSpan.FromHours(100), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Worker_sees_only_own_cards_follows_the_workflow_and_manager_reviews()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var (clientId, reel, editorId) = await RefsAsync(admin);
        var o = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        var ids = o.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();

        // Not assigned yet: invisible to the worker.
        (await editor.GetAsync($"/api/v1/work/{ids[0]}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var suggestions = await Ok(await admin.GetAsync($"/api/v1/work/{ids[0]}/suggestions"));
        suggestions.GetArrayLength().Should().BeGreaterThan(0);
        (await editor.GetAsync($"/api/v1/work/{ids[0]}/suggestions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        async Task<JsonElement> Card(HttpClient c, Guid id) => (await Ok(await c.GetAsync($"/api/v1/work/{id}"))).GetProperty("card");
        async Task<HttpResponseMessage> Move(HttpClient c, Guid id, string status, uint version) =>
            await c.PostAsJsonAsync($"/api/v1/work/{id}/move", new { status, version });

        foreach (var id in ids)
        {
            var v = (await Card(admin, id)).GetProperty("version").GetUInt32();
            await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{id}/assign", new { userId = editorId, version = v }));
        }

        var board = await Ok(await editor.GetAsync("/api/v1/work/board?mine=true"));
        board.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(ids);

        // Stage 2 is waiting for stage 1.
        var c2 = await Card(editor, ids[1]);
        (await Move(editor, ids[1], "inProgress", c2.GetProperty("version").GetUInt32())).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Stage 1: start → done (no review needed) → stage 2 unlocks.
        var c1 = await Card(editor, ids[0]);
        var started = await Ok(await Move(editor, ids[0], "inProgress", c1.GetProperty("version").GetUInt32()));
        (await Move(editor, ids[0], "done", c1.GetProperty("version").GetUInt32())).StatusCode.Should().Be(HttpStatusCode.Conflict); // stale version
        await Ok(await Move(editor, ids[0], "done", started.GetProperty("version").GetUInt32()));
        (await Card(editor, ids[1])).GetProperty("status").GetString().Should().Be("todo");

        // Stage 3 needs admin review then client approval: the worker can't skip review.
        await Ok(await Move(editor, ids[1], "inProgress", (await Card(editor, ids[1])).GetProperty("version").GetUInt32()));
        await Ok(await Move(editor, ids[1], "done", (await Card(editor, ids[1])).GetProperty("version").GetUInt32()));
        await Ok(await Move(editor, ids[2], "inProgress", (await Card(editor, ids[2])).GetProperty("version").GetUInt32()));
        (await Move(editor, ids[2], "done", (await Card(editor, ids[2])).GetProperty("version").GetUInt32())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var detail = await Ok(await editor.GetAsync($"/api/v1/work/{ids[2]}"));
        detail.GetProperty("allowedMoves").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("todo", "inReview");
        await Ok(await Move(editor, ids[2], "inReview", detail.GetProperty("card").GetProperty("version").GetUInt32()));
        (await Move(editor, ids[2], "clientReview", (await Card(editor, ids[2])).GetProperty("version").GetUInt32())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Manager: request changes (with a note), then approve all the way to done → the order completes.
        await Ok(await editor.PostAsJsonAsync($"/api/v1/work/{ids[2]}/comments", new { body = "نسخه‌ی اول آماده است", visibility = "internal" }));
        (await editor.PostAsJsonAsync($"/api/v1/work/{ids[2]}/comments", new { body = "سلام مشتری", visibility = "client" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var changes = await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{ids[2]}/move", new { status = "changesRequested", version = (await Card(admin, ids[2])).GetProperty("version").GetUInt32(), note = "رنگ لوگو را درست کن" }));
        changes.GetProperty("revisionCount").GetInt32().Should().Be(1);
        var withNote = await Ok(await editor.GetAsync($"/api/v1/work/{ids[2]}"));
        withNote.GetProperty("comments").EnumerateArray().Select(c => c.GetProperty("body").GetString()).Should().Contain("رنگ لوگو را درست کن");
        await Ok(await Move(admin, ids[2], "done", (await Card(admin, ids[2])).GetProperty("version").GetUInt32()));
        var order = await Ok(await admin.GetAsync($"/api/v1/orders/{o.GetProperty("id").GetGuid()}"));
        order.GetProperty("status").GetString().Should().Be("completed");
    }

    [Fact]
    public async Task Resumable_upload_checks_offset_type_and_access()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var (clientId, reel, editorId) = await RefsAsync(admin);
        var o = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        var orderId = o.GetProperty("id").GetGuid();
        var itemId = o.GetProperty("stages")[0].GetProperty("id").GetGuid();

        // A PNG in two chunks.
        var png = new byte[3000];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        var start = await Ok(await admin.PostAsJsonAsync("/api/v1/files", new { orderId, workItemId = itemId, purpose = "deliverable", fileName = "../../cover.png", contentType = "image/png", size = png.Length }));
        var fileId = start.GetProperty("id").GetGuid();

        async Task<HttpResponseMessage> Patch(HttpClient c, Guid id, long offset, byte[] data)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/files/{id}/upload") { Content = new ByteArrayContent(data) };
            req.Headers.Add("Upload-Offset", offset.ToString());
            return await c.SendAsync(req);
        }

        (await Ok(await Patch(admin, fileId, 0, png[..1000]))).GetProperty("received").GetInt64().Should().Be(1000);
        (await Patch(admin, fileId, 0, png[..1000])).StatusCode.Should().Be(HttpStatusCode.Conflict);  // wrong offset
        (await Patch(editor, fileId, 1000, png[1000..])).StatusCode.Should().Be(HttpStatusCode.NotFound); // not the uploader
        (await Ok(await admin.GetAsync($"/api/v1/files/{fileId}/upload"))).GetProperty("received").GetInt64().Should().Be(1000); // resume point
        (await Ok(await Patch(admin, fileId, 1000, png[1000..]))).GetProperty("complete").GetBoolean().Should().BeTrue();

        // The worker can't read files of an order they don't work on — until assigned.
        (await editor.GetAsync($"/api/v1/files/{fileId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var v = (await Ok(await admin.GetAsync($"/api/v1/work/{itemId}"))).GetProperty("card").GetProperty("version").GetUInt32();
        await Ok(await admin.PostAsJsonAsync($"/api/v1/work/{itemId}/assign", new { userId = editorId, version = v }));
        var dl = await editor.GetAsync($"/api/v1/files/{fileId}");
        dl.StatusCode.Should().Be(HttpStatusCode.OK);
        dl.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        dl.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("cover.png");
        (await dl.Content.ReadAsByteArrayAsync()).Should().Equal(png);

        // Signed 10-minute link: works without a session, supports Range (resume), refuses tampering; the editor can't link files they can't read.
        var link = await Ok(await admin.PostAsync($"/api/v1/files/{fileId}/link", null));
        var url = link.GetProperty("url").GetString()!;
        var anon = _f.CreateClient();
        var ranged = new HttpRequestMessage(HttpMethod.Get, url);
        ranged.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(10, 19);
        var part = await anon.SendAsync(ranged);
        part.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await part.Content.ReadAsByteArrayAsync()).Should().Equal(png[10..20]);
        (await anon.GetAsync(url[..^4] + "AAAA")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await As("writer")).PostAsync($"/api/v1/files/{fileId}/link", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // HTML pretending to be an image is refused and removed.
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        var s2 = await Ok(await admin.PostAsJsonAsync("/api/v1/files", new { orderId, purpose = "attachment", fileName = "x.png", contentType = "image/png", size = html.Length }));
        var r = await Patch(admin, s2.GetProperty("id").GetGuid(), 0, html);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.GetAsync($"/api/v1/files/{s2.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Change_cursor_returns_only_what_the_user_may_see()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var (clientId, reel, _) = await RefsAsync(admin);
        var start = await Ok(await admin.GetAsync("/api/v1/changes"));
        var cursor = start.GetProperty("cursor").GetInt64();

        var o = await Ok(await admin.PostAsJsonAsync("/api/v1/orders", Order(clientId, reel)));
        var after = await Ok(await admin.GetAsync($"/api/v1/changes?since={cursor}"));
        after.GetProperty("reset").GetBoolean().Should().BeFalse();
        after.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(o.GetProperty("id").GetGuid());

        var editorView = await Ok(await editor.GetAsync($"/api/v1/changes?since={cursor}"));
        editorView.GetProperty("events").GetArrayLength().Should().Be(0);
    }
}
