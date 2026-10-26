using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Cctv;
using Karnama.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karnama.IntegrationTests;

/// <summary>N11: office cameras (PROMPT2 §5, §15).</summary>
public sealed class CctvTests(DemoAppFixture app) : IClassFixture<DemoAppFixture>
{
    private readonly KarnamaFactory _f = app.Factory;
    private async Task<HttpClient> As(string role) => _f.CreateClient().WithToken(await _f.DemoTokenAsync(role));

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return r.Content.Headers.ContentLength == 0 ? default : await r.JsonAsync();
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _f.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Guid> AddCameraAsync(HttpClient admin, string name) =>
        (await Ok(await admin.PostAsJsonAsync("/api/v1/cctv/cameras", new
        {
            name, location = "سالن", sortOrder = 1, isActive = true, workAreaConfirmed = true,
            mainUrl = "rtsp://admin:Sup3rSecret@192.168.1.64:554/Streaming/Channels/101",
            subUrl = "rtsp://admin:Sup3rSecret@192.168.1.64:554/Streaming/Channels/102",
        }))).GetProperty("id").GetGuid();

    /// <summary>MediaMTX calling its auth hook from <paramref name="ip"/>.</summary>
    private async Task<int> HookAsync(object body, string ip = "10.66.0.2")
    {
        var json = JsonSerializer.Serialize(body);
        var ctx = await _f.Server.SendAsync(c =>
        {
            c.Request.Method = "POST";
            c.Request.Path = "/api/v1/cctv/media-auth";
            c.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            var bytes = Encoding.UTF8.GetBytes(json);
            c.Request.Body = new MemoryStream(bytes);
            c.Request.ContentLength = bytes.Length;
            c.Request.ContentType = "application/json";
            c.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new HasBody());
        });
        return ctx.Response.StatusCode;
    }

    [Fact]
    public async Task Cameras_need_an_office_address_and_the_work_area_promise_and_passwords_never_leave_the_vault()
    {
        var admin = await As("admin");
        (await admin.PostAsJsonAsync("/api/v1/cctv/cameras", new { name = "بدون تأیید", sortOrder = 1, isActive = true, workAreaConfirmed = false, mainUrl = "rtsp://192.168.1.5/x" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync("/api/v1/cctv/cameras", new { name = "اینترنتی", sortOrder = 1, isActive = true, workAreaConfirmed = true, mainUrl = "rtsp://8.8.8.8/x" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync("/api/v1/cctv/cameras", new { name = "http", sortOrder = 1, isActive = true, workAreaConfirmed = true, mainUrl = "http://192.168.1.5/x" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var id = await AddCameraAsync(admin, "سالن اصلی");
        var path = await Db(db => db.Cameras.Where(c => c.Id == id).Select(c => c.Path).FirstAsync());
        _f.Media.Paths[path].OnDemand.Should().BeTrue("the full-quality stream is pulled only while someone watches");
        _f.Media.Paths[path + "-low"].Source.Should().EndWith("/Channels/102");
        _f.Media.Paths[path + "-low"].OnDemand.Should().BeFalse();

        var list = await Ok(await admin.GetAsync("/api/v1/cctv"));
        var cam = list.GetProperty("cameras").EnumerateArray().First(c => c.GetProperty("id").GetGuid() == id);
        cam.GetProperty("mainMasked").GetString().Should().Contain("admin:••••@192.168.1.64").And.NotContain("Sup3rSecret");
        list.GetRawText().Should().NotContain("Sup3rSecret");
        var stored = await Db(db => db.Cameras.AsNoTracking().FirstAsync(c => c.Id == id));
        Encoding.UTF8.GetString(stored.MainCipher).Should().NotContain("Sup3rSecret");
        (await (await As("editor")).GetAsync("/api/v1/cctv")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Keep the address when the field is left empty.
        await Ok(await admin.PutAsJsonAsync($"/api/v1/cctv/cameras/{id}", new { name = "سالن", sortOrder = 2, isActive = true, workAreaConfirmed = false }));
        _f.Media.Paths[path].Source.Should().Contain("Sup3rSecret");
    }

    [Fact]
    public async Task Snapshots_are_shared_for_ten_seconds_and_only_for_viewers()
    {
        var admin = await As("admin");
        var id = await AddCameraAsync(admin, "ورودی");
        var before = _f.Media.Snapshots;
        var a = await admin.GetAsync($"/api/v1/cctv/cameras/{id}/snapshot");
        a.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        await admin.GetAsync($"/api/v1/cctv/cameras/{id}/snapshot");
        (_f.Media.Snapshots - before).Should().Be(1);
        (await (await As("editor")).GetAsync($"/api/v1/cctv/cameras/{id}/snapshot")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Live_video_needs_a_short_token_for_that_one_camera_and_every_view_is_audited()
    {
        var admin = await As("admin");
        var id = await AddCameraAsync(admin, "انبار");
        var other = await AddCameraAsync(admin, "دفتر مدیر");
        var path = await Db(db => db.Cameras.Where(c => c.Id == id).Select(c => c.Path).FirstAsync());
        var otherPath = await Db(db => db.Cameras.Where(c => c.Id == other).Select(c => c.Path).FirstAsync());

        var live = await Ok(await admin.PostAsync($"/api/v1/cctv/cameras/{id}/live?high=true", null));
        live.GetProperty("whepUrl").GetString().Should().Be($"https://cctv.test/{path}/whep");
        var token = live.GetProperty("token").GetString();

        (await HookAsync(new { action = "read", path, token })).Should().Be(200);
        (await HookAsync(new { action = "read", path, token }, ip: "5.5.5.5")).Should().Be(403);
        (await HookAsync(new { action = "read", path = otherPath, token })).Should().Be(401);
        (await HookAsync(new { action = "publish", path, token })).Should().Be(401);
        (await HookAsync(new { action = "read", path, token = "forged" })).Should().Be(401);
        (await HookAsync(new { action = "read", path, query = $"token={Uri.EscapeDataString(token!)}" })).Should().Be(200);
        (await HookAsync(new { user = "internal", password = "internal-secret", action = "read", path })).Should().Be(200);
        (await HookAsync(new { user = "internal", password = "guess", action = "api" })).Should().Be(401);

        // An expired token (same camera) is refused.
        var signer = _f.Services.GetRequiredService<Karnama.Application.Abstractions.ILinkSigner>();
        var old = signer.Sign($"cctv|{path}|{Guid.NewGuid()}|{Guid.NewGuid():N}", TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        (await HookAsync(new { action = "read", path, token = old })).Should().Be(401);

        await Ok(await admin.PostAsJsonAsync($"/api/v1/cctv/cameras/{id}/live/stop", new { sessionId = live.GetProperty("sessionId").GetGuid() }));
        (await Db(db => db.AuditLogs.CountAsync(a => a.Action == "cctv.liveStarted" && a.EntityId == id.ToString()))).Should().Be(1);
        var stopped = await Db(db => db.AuditLogs.AsNoTracking().FirstAsync(a => a.Action == "cctv.liveStopped" && a.EntityId == id.ToString()));
        stopped.DataJson.Should().Contain("\"seconds\":");
        (await (await As("editor")).PostAsync($"/api/v1/cctv/cameras/{id}/live", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_camera_that_drops_warns_the_managers_once()
    {
        var admin = await As("admin");
        var id = await AddCameraAsync(admin, "پارکینگ");
        var path = await Db(db => db.Cameras.Where(c => c.Id == id).Select(c => c.Path).FirstAsync());
        async Task Health()
        {
            using var scope = _f.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CctvService>().HealthAsync(default);
        }
        await Health();
        (await Db(db => db.Cameras.Where(c => c.Id == id).Select(c => c.Online).FirstAsync())).Should().BeTrue();
        _f.Media.Down.Add(path + "-low");
        await Health();
        await Health();
        (await Db(db => db.Notifications.Where(n => n.Kind == "cctv.offline" && n.Title.Contains("پارکینگ")).Select(n => n.EventKey).Distinct().CountAsync())).Should().Be(1);
        _f.Media.Down.Clear();
    }

    [Fact]
    public async Task A_box_that_cannot_be_reached_is_reported_and_stop_needs_a_real_camera()
    {
        var admin = await As("admin");
        var id = await AddCameraAsync(admin, "راهرو");
        var before = await Db(db => db.Cameras.CountAsync());
        static async Task<string?> Code(HttpResponseMessage r)
        {
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
            return (await r.JsonAsync()).GetProperty("code").GetString();
        }

        _f.Media.Unreachable = true;
        try
        {
            (await Code(await admin.PostAsJsonAsync("/api/v1/cctv/cameras", new
            {
                name = "حیاط", location = "بیرون", sortOrder = 2, isActive = true, workAreaConfirmed = true, mainUrl = "rtsp://192.168.1.70:554/main",
            }))).Should().Be("cctv.unreachable");
            (await Db(db => db.Cameras.CountAsync())).Should().Be(before, "nothing is saved for a box that is down");
            (await Code(await admin.PostAsync("/api/v1/cctv/resync", null))).Should().Be("cctv.unreachable");
            (await Code(await admin.PostAsync($"/api/v1/cctv/cameras/{id}/live", null))).Should().Be("cctv.unreachable");
            // Snapshot keeps its own answer: no frame.
            (await Code(await admin.GetAsync($"/api/v1/cctv/cameras/{id}/snapshot"))).Should().Be("cctv.noFrame");
        }
        finally
        {
            _f.Media.Unreachable = false;
        }
        (await Ok(await admin.PostAsync("/api/v1/cctv/resync", null))).GetInt32().Should().BeGreaterThan(0);

        var unknown = Guid.NewGuid();
        (await admin.PostAsJsonAsync($"/api/v1/cctv/cameras/{unknown}/live/stop", new { sessionId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Db(db => db.AuditLogs.CountAsync(a => a.Action == "cctv.liveStopped" && a.EntityId == unknown.ToString()))).Should().Be(0);
    }

    private sealed class HasBody : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
