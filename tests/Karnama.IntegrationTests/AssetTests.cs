using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Assets;
using Karnama.Domain.Assets;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N3: equipment lent to staff (PROMPT2 §3, §15).</summary>
public sealed class AssetTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
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

    private Task<Guid> UserId(string phone) => Db(db => db.Users.Where(u => u.Phone == phone).Select(u => u.Id).FirstAsync());

    private async Task<JsonElement> NewAsset(HttpClient admin, string name, string? tag = null) =>
        await Ok(await admin.PostAsJsonAsync("/api/v1/assets", new { name, categoryId = AssetDefaults.Camera, brand = "Canon", serial = $"S-{Guid.NewGuid():N}"[..12], tag }));

    private static Guid Id(JsonElement d) => d.GetProperty("asset").GetProperty("id").GetGuid();
    private static uint Version(JsonElement d) => d.GetProperty("asset").GetProperty("version").GetUInt32();
    private static Guid Current(JsonElement d) => d.GetProperty("current").GetProperty("id").GetGuid();

    [Fact]
    public async Task Hand_over_accept_return_with_damage_and_confirm_keeps_a_full_history()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var ali = await UserId("09000000002");
        var a = await NewAsset(admin, "دوربین تست تحویل");
        a.GetProperty("asset").GetProperty("tag").GetString().Should().StartWith("KN-");

        var h = await Ok(await admin.PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover",
            new { userId = ali, expectedReturn = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"), condition = "سالم", accessories = new[] { "باتری ×۲", "بند" }, version = Version(a) }));
        h.GetProperty("current").GetProperty("state").GetString().Should().Be("pendingAcceptance");
        var assignment = Current(h);

        // Only the receiver accepts; the worker sees it in "پیش من".
        (await admin.PostAsync($"/api/v1/assets/assignments/{assignment}/accept", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Ok(await editor.GetAsync("/api/v1/assets/mine"))).GetProperty("holding").EnumerateArray().Should().Contain(x => x.GetProperty("id").GetGuid() == Id(a));
        var accepted = await Ok(await editor.PostAsync($"/api/v1/assets/assignments/{assignment}/accept", null));
        accepted.GetProperty("current").GetProperty("state").GetString().Should().Be("active");
        accepted.GetProperty("current").GetProperty("acceptedAt").ValueKind.Should().Be(JsonValueKind.String);

        await Ok(await editor.PostAsJsonAsync($"/api/v1/assets/assignments/{assignment}/return", new { condition = "خط روی لنز", damage = "درپوش لنز گم شد" }));
        var done = await Ok(await admin.PostAsJsonAsync($"/api/v1/assets/assignments/{assignment}/return/confirm", new { status = "inRepair" }));
        done.GetProperty("asset").GetProperty("status").GetString().Should().Be("inRepair");
        done.GetProperty("asset").GetProperty("holderId").ValueKind.Should().Be(JsonValueKind.Null);
        done.GetProperty("past")[0].GetProperty("damage").GetString().Should().Be("درپوش لنز گم شد");
        done.GetProperty("history").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).Should()
            .ContainInOrder("returned", "returnSubmitted", "accepted", "handedOver", "created");
        (await Db(db => db.AuditLogs.CountAsync(x => x.Action == "assets.returned" && x.EntityId == Id(a).ToString()))).Should().Be(1);
        // In repair → can't be handed over until it's back on the shelf.
        (await admin.PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover", new { userId = ali, version = Version(done) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Two_simultaneous_hand_overs_of_one_asset_only_one_wins()
    {
        var admin = await As("admin");
        var a = await NewAsset(admin, "لپ‌تاپ همزمانی");
        var ali = await UserId("09000000002");
        var maryam = await UserId("09000000003");
        var other = await As("admin");
        var results = await Task.WhenAll(
            admin.PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover", new { userId = ali, version = Version(a) }),
            other.PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover", new { userId = maryam, version = Version(a) }));
        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await Db(db => db.AssetAssignments.CountAsync(x => x.AssetId == Id(a) && x.State != AssignmentState.Cancelled))).Should().Be(1);

        // Even past the application checks, the database refuses a second open hand-over.
        var act = () => Db(async db =>
        {
            var asset = await db.Assets.AsNoTracking().FirstAsync(x => x.Id == Id(a));
            var fake = AssetAssignment.HandOver(Asset.Create(new AssetSpec("x", AssetDefaults.Camera, null, null, null, "X-1", null, null, null, null), "X-1").Value!,
                ali, ali, null, null, null, DateTimeOffset.UtcNow).Value!;
            db.Entry(fake).Property(nameof(AssetAssignment.AssetId)).CurrentValue = asset.Id;
            db.AssetAssignments.Add(fake);
            return await db.SaveChangesAsync();
        });
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Workers_see_only_what_they_hold_and_cannot_manage()
    {
        var admin = await As("admin");
        var writer = await As("writer");
        var ali = await UserId("09000000002");
        var a = await NewAsset(admin, "میکروفون دید");
        var h = await Ok(await admin.PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover", new { userId = ali, version = Version(a) }));

        (await writer.GetAsync($"/api/v1/assets/{Id(a)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await writer.GetAsync($"/api/v1/assets/tag/{a.GetProperty("asset").GetProperty("tag").GetString()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Ok(await writer.GetAsync("/api/v1/assets"))).EnumerateArray().Should().NotContain(x => x.GetProperty("id").GetGuid() == Id(a));
        (await writer.PostAsync($"/api/v1/assets/assignments/{Current(h)}/accept", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await writer.PostAsJsonAsync("/api/v1/assets", new { name = "x", categoryId = AssetDefaults.Camera })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await As("editor")).PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover", new { userId = ali, version = 0 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await As("client")).GetAsync("/api/v1/assets/mine")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/v1/assets", new { name = "تکراری", categoryId = AssetDefaults.Camera, tag = a.GetProperty("asset").GetProperty("tag").GetString() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/v1/assets/{Id(a)}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Direct_transfer_needs_the_receiver_to_accept()
    {
        var admin = await As("admin");
        var editor = await As("editor");
        var writer = await As("writer");
        var ali = await UserId("09000000002");
        var maryam = await UserId("09000000003");
        var a = await NewAsset(admin, "گیمبال انتقال");
        var h = await Ok(await admin.PostAsJsonAsync($"/api/v1/assets/{Id(a)}/handover", new { userId = ali, version = Version(a) }));
        await Ok(await editor.PostAsync($"/api/v1/assets/assignments/{Current(h)}/accept", null));

        (await writer.PostAsJsonAsync($"/api/v1/assets/assignments/{Current(h)}/transfer", new { userId = maryam })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var offered = await Ok(await editor.PostAsJsonAsync($"/api/v1/assets/assignments/{Current(h)}/transfer", new { userId = maryam }));
        offered.GetProperty("current").GetProperty("transferToId").GetGuid().Should().Be(maryam);
        (await Ok(await writer.GetAsync("/api/v1/assets/mine"))).GetProperty("incoming").EnumerateArray().Should().Contain(x => x.GetProperty("id").GetGuid() == Id(a));
        var moved = await Ok(await writer.PostAsJsonAsync($"/api/v1/assets/assignments/{Current(h)}/transfer/accept", new { condition = "سالم" }));
        moved.GetProperty("asset").GetProperty("holderId").GetGuid().Should().Be(maryam);
        moved.GetProperty("current").GetProperty("state").GetString().Should().Be("active");
        (await Db(db => db.AssetAssignments.CountAsync(x => x.AssetId == Id(a) && (x.State == AssignmentState.Active || x.State == AssignmentState.PendingAcceptance)))).Should().Be(1);
        (await editor.GetAsync($"/api/v1/assets/{Id(a)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Photos_must_be_images_and_stay_private()
    {
        var admin = await As("admin");
        var a = await NewAsset(admin, "لنز عکس");
        static MultipartFormDataContent Form(byte[] bytes)
        {
            var f = new MultipartFormDataContent();
            var c = new ByteArrayContent(bytes);
            c.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            f.Add(c, "file", "p.png");
            return f;
        }
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0x1F, 0x15, 0xC4, 0x89];
        (await admin.PostAsync($"/api/v1/assets/{Id(a)}/photos?stage=Profile", Form("MZ fake exe"u8.ToArray()))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var photo = await Ok(await admin.PostAsync($"/api/v1/assets/{Id(a)}/photos?stage=Profile", Form(png)));
        var get = await admin.GetAsync($"/api/v1/assets/photos/{photo.GetProperty("id").GetGuid()}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await (await As("writer")).GetAsync($"/api/v1/assets/photos/{photo.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await As("editor")).PostAsync($"/api/v1/assets/{Id(a)}/photos?stage=Profile", Form(png))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Alerts_overdue_and_warranty_once_per_day()
    {
        var ali = await UserId("09000000002");
        async Task<int> Run()
        {
            using var scope = _f.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AssetService>().AlertsAsync(CancellationToken.None);
        }
        (await Run()).Should().BeGreaterThan(0);
        (await Db(db => db.Notifications.CountAsync(n => n.UserId == ali && n.Kind == "assets.overdue"))).Should().Be(1);
        (await Db(db => db.Notifications.CountAsync(n => n.Kind == "assets.warranty"))).Should().BeGreaterThan(0);
        var before = await Db(db => db.Notifications.CountAsync());
        await Run();
        (await Db(db => db.Notifications.CountAsync())).Should().Be(before);
    }
}
