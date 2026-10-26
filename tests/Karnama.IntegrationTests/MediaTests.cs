using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karnama.Domain.Media;
using Karnama.Domain.Notify;
using Karnama.Infrastructure.Notify;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N5: files bot (PROMPT2 §8 / §15): unknown senders, duplicates, archive → admins → assign → delivered, failures.</summary>
public sealed class MediaTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private static long _update = 700_000;
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

    private async Task<HttpResponseMessage> PostUpdateAsync(string body)
    {
        var now = _f.Clock.GetUtcNow().ToUnixTimeSeconds();
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bots/telegram") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        r.Headers.Add(GatewaySignature.TimestampHeader, now.ToString(System.Globalization.CultureInfo.InvariantCulture));
        r.Headers.Add(GatewaySignature.SignatureHeader, GatewaySignature.Compute(KarnamaFactory.GatewaySecret, now, body));
        return await _f.CreateClient().SendAsync(r);
    }

    private static string Video(long updateId, long chatId, string unique, int messageId = 55) => JsonSerializer.Serialize(new
    {
        update_id = updateId,
        message = new
        {
            message_id = messageId,
            date = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            chat = new { id = chatId, type = "private" },
            from = new { id = chatId },
            caption = "راش‌های کافه",
            video = new { file_id = $"BAACAgQ-{unique}", file_unique_id = unique, file_size = 48_000_000, duration = 95, mime_type = "video/mp4", thumbnail = new { file_id = $"thumb-{unique}", file_unique_id = $"t{unique}" } },
        },
    });

    private static string Callback(long updateId, long chatId, string data) => JsonSerializer.Serialize(new
    {
        update_id = updateId,
        callback_query = new { id = $"cb{updateId}", from = new { id = chatId }, data, message = new { message_id = 9, chat = new { id = chatId, type = "private" } } },
    });

    private async Task<Guid> LinkAsync(string phone, long chatId) => await Db(async db =>
    {
        var id = await db.Users.Where(u => u.Phone == phone).Select(u => u.Id).FirstAsync();
        await db.MessengerLinks.Where(l => l.UserId == id && l.Messenger == Messenger.Telegram).ExecuteDeleteAsync();
        db.MessengerLinks.Add(new MessengerLink(id, Messenger.Telegram, chatId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return id;
    });

    /// <summary>Drains what is due now (the outbox sends 50 at a time).</summary>
    private async Task DispatchAsync()
    {
        var d = new OutboxDispatcher(_f.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxDispatcher>.Instance);
        for (var i = 0; i < 10 && await d.DispatchOnceAsync(default) > 0; i++) { }
    }

    [Fact]
    public async Task An_unlinked_sender_gets_one_line_back_and_nothing_is_stored()
    {
        var before = await Db(db => db.MediaAssets.CountAsync());
        (await PostUpdateAsync(Video(Interlocked.Increment(ref _update), 999_111, "unlinked-file"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Db(db => db.MediaAssets.CountAsync())).Should().Be(before);
        await DispatchAsync();
        _f.Bots.Calls.Should().Contain(c => c.Method == "sendMessage" && c.Args["chat_id"]!.GetValue<long>() == 999_111 && c.Args["text"]!.GetValue<string>().Contains("وصل"));
    }

    [Fact]
    public async Task A_file_is_recorded_once_archived_sent_to_admins_and_delivered_to_the_assigned_worker()
    {
        var editor = await LinkAsync("09000000002", 610_002);
        var adminId = await LinkAsync("09000000001", 610_001);
        var writer = await LinkAsync("09000000003", 610_003);

        var update = Interlocked.Increment(ref _update);
        var body = Video(update, 610_002, "rush-cafe-1");
        await Ok(await PostUpdateAsync(body));
        await Ok(await PostUpdateAsync(body));                                                          // same update twice
        await Ok(await PostUpdateAsync(Video(Interlocked.Increment(ref _update), 610_002, "rush-cafe-1", 56)));   // same file again
        var assets = await Db(db => db.MediaAssets.Where(m => m.FileUniqueId == "rush-cafe-1").ToListAsync());
        assets.Should().HaveCount(1, "update_id and file_unique_id both prevent duplicates");
        var asset = assets[0];
        asset.SenderId.Should().Be(editor);
        asset.FileSize.Should().Be(48_000_000);
        asset.Caption.Should().Be("راش‌های کافه");

        await DispatchAsync();   // archive copy + the "which work?" question
        _f.Bots.Calls.Should().Contain(c => c.Method == "copyMessage" && c.Args["chat_id"]!.GetValue<long>() == KarnamaFactory.ArchiveChat && c.Args["from_chat_id"]!.GetValue<long>() == 610_002);
        var question = _f.Bots.Calls.Last(c => c.Method == "sendMessage" && c.Args["chat_id"]!.GetValue<long>() == 610_002 && c.Args["reply_markup"] is not null);
        question.Args["reply_markup"]!["inline_keyboard"]!.AsArray().Count.Should().BeGreaterThan(1, "the editor's open work items are offered");
        await DispatchAsync();   // admin copies (from the archive) + thumbnail
        var archived = await Db(db => db.MediaAssets.AsNoTracking().FirstAsync(m => m.Id == asset.Id));
        archived.ArchiveChatId.Should().Be(KarnamaFactory.ArchiveChat);
        archived.Status.Should().Be(MediaStatus.SentToAdmins);
        archived.ThumbPath.Should().NotBeNull();
        _f.Bots.Calls.Should().Contain(c => c.Method == "copyMessage" && c.Args["chat_id"]!.GetValue<long>() == 610_001 && c.Args["from_chat_id"]!.GetValue<long>() == KarnamaFactory.ArchiveChat);

        // The editor taps one of its work items.
        var firstWork = question.Args["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!.GetValue<string>();
        await Ok(await PostUpdateAsync(Callback(Interlocked.Increment(ref _update), 610_002, firstWork)));
        (await Db(db => db.MediaAssets.Where(m => m.Id == asset.Id).Select(m => m.WorkItemId).FirstAsync())).Should().NotBeNull();

        // Admin assigns it in the panel to the writer → the bot copies it straight into the writer's chat.
        var admin = await As("admin");
        var targets = await Ok(await admin.GetAsync("/api/v1/media/targets"));
        targets.GetProperty("staff").EnumerateArray().Should().Contain(s => s.GetProperty("id").GetGuid() == writer && s.GetProperty("linked").GetBoolean());
        var assigned = await Ok(await admin.PostAsJsonAsync($"/api/v1/media/{asset.Id}/assign", new { toUserId = writer, workItemId = (Guid?)null, toClient = false }));
        assigned.GetProperty("status").GetString().Should().Be("assigned");
        await DispatchAsync();
        var delivered = _f.Bots.Calls.Last(c => c.Method == "copyMessage" && c.Args["chat_id"]!.GetValue<long>() == 610_003);
        delivered.Args["caption"]!.GetValue<string>().Should().Contain("برای کار").And.NotContain("0900", "captions never carry phone numbers");
        (await Db(db => db.MediaAssets.Where(m => m.Id == asset.Id).Select(m => m.Status).FirstAsync())).Should().Be(MediaStatus.Delivered);

        // The inbox lists it with the work and who has it.
        var list = await Ok(await admin.GetAsync("/api/v1/media"));
        var row = list.GetProperty("items").EnumerateArray().First(i => i.GetProperty("id").GetGuid() == asset.Id);
        row.GetProperty("assignedToId").GetGuid().Should().Be(writer);
        row.GetProperty("workTitle").GetString().Should().NotBeNullOrEmpty();
        (await admin.GetAsync($"/api/v1/media/{asset.Id}/thumb")).StatusCode.Should().Be(HttpStatusCode.OK);
        adminId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Admin_can_assign_from_inside_telegram_and_a_blocked_bot_is_reported()
    {
        await LinkAsync("09000000002", 620_002);
        await LinkAsync("09000000001", 620_001);
        var writer = await LinkAsync("09000000003", 620_003);
        await Ok(await PostUpdateAsync(Video(Interlocked.Increment(ref _update), 620_002, "rush-blocked")));
        await DispatchAsync();
        await DispatchAsync();
        var asset = await Db(db => db.MediaAssets.FirstAsync(m => m.FileUniqueId == "rush-blocked"));

        // "Assign to…" in the admin's copy → list of people → pick the writer.
        var adminCopy = _f.Bots.Calls.Last(c => c.Method == "copyMessage" && c.Args["chat_id"]!.GetValue<long>() == 620_001
                                                && c.Args["reply_markup"]!.ToJsonString().Contains(Karnama.Application.Media.MediaService.ShortId(asset.Id)));
        var assignButton = adminCopy.Args["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!.GetValue<string>();
        await Ok(await PostUpdateAsync(Callback(Interlocked.Increment(ref _update), 620_001, assignButton)));
        await DispatchAsync();
        var people = _f.Bots.Calls.Last(c => c.Method == "sendMessage" && c.Args["chat_id"]!.GetValue<long>() == 620_001);
        var pick = people.Args["reply_markup"]!["inline_keyboard"]!.AsArray()
            .Select(r => r![0]!["callback_data"]!.GetValue<string>()).First(d => d.EndsWith(Karnama.Application.Media.MediaService.ShortId(writer)));

        _f.Bots.Blocked.Add(620_003);
        await Ok(await PostUpdateAsync(Callback(Interlocked.Increment(ref _update), 620_001, pick)));
        await DispatchAsync();
        (await Db(db => db.MediaAssets.Where(m => m.Id == asset.Id).Select(m => m.Status).FirstAsync())).Should().Be(MediaStatus.Failed);
        (await Db(db => db.Notifications.CountAsync(n => n.Kind == "media.failed"))).Should().BeGreaterThan(0);

        // A worker can't use the inbox or assign by pressing someone else's button.
        (await (await As("editor")).GetAsync("/api/v1/media")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await Ok(await PostUpdateAsync(Callback(Interlocked.Increment(ref _update), 620_002, pick)));
        await DispatchAsync();
        _f.Bots.Calls.Should().Contain(c => c.Method == "answerCallbackQuery" && c.Args["text"]!.GetValue<string>().Contains("اجازه"));
    }

    [Fact]
    public async Task Assigning_to_someone_without_the_bot_is_refused()
    {
        var admin = await As("admin");
        await LinkAsync("09000000002", 630_002);
        await Ok(await PostUpdateAsync(Video(Interlocked.Increment(ref _update), 630_002, "rush-nolink")));
        var asset = await Db(db => db.MediaAssets.FirstAsync(m => m.FileUniqueId == "rush-nolink"));
        var unlinked = await Db(async db =>
        {
            var linked = await db.MessengerLinks.Select(l => l.UserId).ToListAsync();
            return await db.Users.Where(u => u.Kind == Karnama.Domain.Identity.UserKind.Staff && u.IsActive && !linked.Contains(u.Id)).Select(u => u.Id).FirstAsync();
        });
        (await admin.PostAsJsonAsync($"/api/v1/media/{asset.Id}/assign", new { toUserId = unlinked, workItemId = (Guid?)null, toClient = false }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
