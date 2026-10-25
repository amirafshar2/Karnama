using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N6: chat (PROMPT2 §7): who may talk to whom, membership (404), edits, read marks, files, search.</summary>
public sealed class ChatTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return r.StatusCode == HttpStatusCode.NoContent || r.Content.Headers.ContentLength == 0 ? default : await r.JsonAsync();
    }

    private async Task<Guid> UserAsync(string phone)
    {
        using var scope = _f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Phone == phone).Select(u => u.Id).FirstAsync();
    }

    [Fact]
    public async Task Worker_talks_to_a_manager_messages_are_private_and_read_marks_work()
    {
        var editor = await As("editor");
        var admin = await As("admin");
        var adminId = await UserAsync("09000000001");
        var writerId = await UserAsync("09000000003");

        var contacts = await Ok(await editor.GetAsync("/api/v1/chat/contacts"));
        contacts.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(adminId).And.NotContain(writerId, "worker↔worker is off by default");
        (await editor.PostAsJsonAsync("/api/v1/chat/direct", new { userId = writerId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var conv = await Ok(await editor.PostAsJsonAsync("/api/v1/chat/direct", new { userId = adminId }));
        var id = conv.GetProperty("id").GetGuid();
        (await Ok(await admin.PostAsJsonAsync("/api/v1/chat/direct", new { userId = await UserAsync("09000000002") }))).GetProperty("id").GetGuid().Should().Be(id, "one chat per pair");

        var key = Guid.NewGuid();
        var sent = await Ok(await editor.PostAsJsonAsync($"/api/v1/chat/{id}/messages", new { body = "سلام، راش‌ها آماده است", clientKey = key }));
        await Ok(await editor.PostAsJsonAsync($"/api/v1/chat/{id}/messages", new { body = "سلام، راش‌ها آماده است", clientKey = key }));   // re-sent after a drop
        var page = await Ok(await admin.GetAsync($"/api/v1/chat/{id}/messages"));
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        (await Ok(await admin.GetAsync("/api/v1/chat/unread"))).GetProperty("count").GetInt32().Should().BeGreaterThan(0);

        // Only new ones since the cursor.
        var lastId = sent.GetProperty("id").GetInt64();
        (await Ok(await admin.GetAsync($"/api/v1/chat/{id}/messages?after={lastId}"))).GetProperty("items").GetArrayLength().Should().Be(0);

        await Ok(await admin.PostAsJsonAsync($"/api/v1/chat/{id}/read", new { upTo = lastId }));
        (await Ok(await editor.GetAsync($"/api/v1/chat/{id}/messages"))).GetProperty("othersReadUpTo").GetInt64().Should().Be(lastId);

        // Someone outside the chat: 404, never the content.
        var writer = await As("writer");
        (await writer.GetAsync($"/api/v1/chat/{id}/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await writer.PostAsJsonAsync($"/api/v1/chat/{id}/messages", new { body = "x", clientKey = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await writer.PutAsJsonAsync($"/api/v1/chat/messages/{lastId}", new { body = "hack" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Edit within 15 minutes by the author only; delete is soft.
        (await admin.PutAsJsonAsync($"/api/v1/chat/messages/{lastId}", new { body = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Ok(await editor.PutAsJsonAsync($"/api/v1/chat/messages/{lastId}", new { body = "سلام، راش‌های کافه آماده است" }))).GetProperty("editedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        using (var scope = _f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ChatMessages.Where(m => m.Id == lastId).ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, m => m.CreatedAt.AddMinutes(-20)));
        }
        (await editor.PutAsJsonAsync($"/api/v1/chat/messages/{lastId}", new { body = "دیر" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await Ok(await editor.DeleteAsync($"/api/v1/chat/messages/{lastId}"));
        var after = (await Ok(await admin.GetAsync($"/api/v1/chat/{id}/messages"))).GetProperty("items")[0];
        after.GetProperty("deleted").GetBoolean().Should().BeTrue();
        after.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_client_only_talks_to_managers_and_never_joins_groups()
    {
        var client = await As("client");
        var editorId = await UserAsync("09000000002");
        var clientUser = await UserAsync("09000000004");
        var contacts = await Ok(await client.GetAsync("/api/v1/chat/contacts"));
        contacts.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().NotContain(editorId);
        (await client.PostAsJsonAsync("/api/v1/chat/direct", new { userId = editorId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/chat/groups", new { title = "x", members = new[] { editorId } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await As("admin");
        var group = await Ok(await admin.PostAsJsonAsync("/api/v1/chat/groups", new { title = "تیم تدوین", members = new[] { editorId, clientUser } }));
        group.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().Contain(editorId).And.NotContain(clientUser);
        (await (await As("editor")).PostAsJsonAsync("/api/v1/chat/groups", new { title = "y", members = Array.Empty<Guid>() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // With the client chat switched off, managers don't see clients either.
        await Ok(await admin.PutAsJsonAsync("/api/v1/settings/chat", new { workerToWorker = true, clientChat = false }));
        (await Ok(await admin.GetAsync("/api/v1/chat/contacts"))).EnumerateArray().Should().NotContain(c => c.GetProperty("isClient").GetBoolean());
        (await Ok(await (await As("editor")).GetAsync("/api/v1/chat/contacts"))).EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .Should().Contain(await UserAsync("09000000003"), "worker↔worker switched on");
        await Ok(await admin.PutAsJsonAsync("/api/v1/settings/chat", new { workerToWorker = false, clientChat = true }));
    }

    [Fact]
    public async Task Work_chat_voice_and_files_follow_the_rules()
    {
        var admin = await As("admin");
        var order = (await Ok(await admin.GetAsync("/api/v1/orders?limit=5"))).GetProperty("items")[0].GetProperty("id").GetGuid();
        var conv = await Ok(await admin.PostAsync($"/api/v1/chat/orders/{order}", null));
        conv.GetProperty("kind").GetString().Should().Be("work");
        var id = conv.GetProperty("id").GetGuid();
        (await Ok(await admin.PostAsync($"/api/v1/chat/orders/{order}", null))).GetProperty("id").GetGuid().Should().Be(id);
        var members = conv.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ToList();
        var outsiderRole = members.Contains(await UserAsync("09000000003")) ? "editor" : "writer";
        if (members.Contains(await UserAsync("09000000002")) && outsiderRole == "editor") outsiderRole = "client";
        (await (await As("client")).PostAsync($"/api/v1/chat/orders/{order}", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        async Task<HttpResponseMessage> Upload(byte[] bytes, string name, bool voice = false, int? duration = null)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", name);
            var q = voice ? $"?voice=true&duration={duration}" : "";
            return await admin.PostAsync($"/api/v1/chat/{id}/files{q}", form);
        }
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        (await Ok(await Upload(png, "a.png"))).GetProperty("kind").GetString().Should().Be("image");
        var ogg = "OggS"u8.ToArray().Concat(new byte[40]).ToArray();
        (await Upload(ogg, "v.ogg", voice: true, duration: 300)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "voice is at most 2 minutes");
        var voice = await Ok(await Upload(ogg, "v.ogg", voice: true, duration: 42));
        voice.GetProperty("kind").GetString().Should().Be("voice");
        (await Upload("<html><script>alert(1)</script>"u8.ToArray(), "x.html")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var get = await admin.GetAsync($"/api/v1/chat/files/{voice.GetProperty("id").GetInt64()}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.Should().Be("audio/ogg");
        (await (await As(outsiderRole)).GetAsync($"/api/v1/chat/files/{voice.GetProperty("id").GetInt64()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await Ok(await admin.PostAsJsonAsync($"/api/v1/chat/{id}/messages", new { body = "لطفاً کاور را عوض کنید", clientKey = Guid.NewGuid() }));
        var hits = await Ok(await admin.GetAsync("/api/v1/chat/search?q=" + Uri.EscapeDataString("کاور")));
        hits.EnumerateArray().Should().Contain(h => h.GetProperty("conversationId").GetGuid() == id);
        (await Ok(await (await As(outsiderRole)).GetAsync("/api/v1/chat/search?q=" + Uri.EscapeDataString("کاور")))).EnumerateArray()
            .Should().NotContain(h => h.GetProperty("conversationId").GetGuid() == id);
    }
}
